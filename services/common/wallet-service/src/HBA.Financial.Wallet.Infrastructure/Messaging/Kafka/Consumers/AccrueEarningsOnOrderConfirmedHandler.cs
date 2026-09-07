using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Financial.Billing.Contracts;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Pricing;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Food.Contracts;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Wallet.Application.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// À la confirmation d'une commande, comptabilise le gain de chaque ligne pour son
/// vendeur.
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Wallet.Application.Earnings.AccrueEarningsOnOrderConfirmedHandler")]
public sealed class AccrueEarningsOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly WalletMutations _wallets;
    private readonly PricingOptions _pricing;
    private readonly ICommissionModuleApi _commissions;
    private readonly IFoodModuleApi _food;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<AccrueEarningsOnOrderConfirmedHandler> _logger;

    public AccrueEarningsOnOrderConfirmedHandler(
        ISellerEarningRepository earningRepository,
        IOrderingModuleApi orderingModuleApi,
        WalletMutations wallets,
        PricingOptions pricing,
        ICommissionModuleApi commissions,
        IFoodModuleApi food,
        IWalletUnitOfWork unitOfWork,
        ILogger<AccrueEarningsOnOrderConfirmedHandler> logger)
    {
        _earningRepository = earningRepository;
        _orderingModuleApi = orderingModuleApi;
        _wallets = wallets;
        _pricing = pricing;
        _commissions = commissions;
        _food = food;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// LA CATÉGORIE N'EST PAS CONNUE ICI : LES RÈGLES « CATÉGORIE » SONT INERTES.
    /// </summary>
    private static readonly Guid CategorieInconnue = Guid.Empty;

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // UNE COMMANDE DE REPAS SE CRÉDITE, MAIS PAS AU MÊME BÉNÉFICIAIRE NI AU
        // MÊME TAUX.
        if (string.Equals(integrationEvent.Kind, "Food", StringComparison.Ordinal))
        {
            await AccruerRestaurationAsync(integrationEvent, cancellationToken);
            return;
        }

        if (await _earningRepository.ExistsForOrderAsync(integrationEvent.OrderId, cancellationToken))
        {
            return;
        }

        var order = await _orderingModuleApi.GetOrderAsync(integrationEvent.OrderId, cancellationToken);

        // CET ABANDON ÉTAIT SILENCIEUX, SUR UNE COMMANDE DÉJÀ ENCAISSÉE.
        if (order is null)
        {
            _logger.LogError(
                "Commande {OrderId} confirmée mais INTROUVABLE : aucun gain comptabilisé, rejeu demandé.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande {integrationEvent.OrderId} introuvable : comptabilisation des gains impossible.");
        }

        if (order.Lines.Count == 0)
        {
            _logger.LogWarning(
                "Commande {OrderId} confirmée SANS ligne : rien à comptabiliser.", integrationEvent.OrderId);

            return;
        }

        var provRate = _pricing.ProviderFeeRate;

        // LE DIVISEUR RESTE LE BARÈME D'AFFICHAGE, PAS LE TAUX DU MOTEUR.
        var facteurAffichage = 1 + _pricing.PlatformCommissionRate + provRate;

        var netBySeller = new Dictionary<Guid, decimal>();
        var totalCommission = 0m;
        var totalProvider = 0m;

        // Le BRUT effectivement comptabilisé, accumulé indépendamment de sa
        // répartition.
        var brutComptabilise = 0m;

        foreach (var line in order.Lines)
        {
            var gross = (line.UnitBasePrice - line.SellerDiscount) * line.Quantity;
            if (gross < 0m)
            {
                gross = 0m;
            }

            // Le prix vendeur net : la base sur laquelle la commission se calcule,
            // et donc celle que le moteur doit recevoir.
            var baseVendeur = gross / facteurAffichage;

            // LE TAUX VIENT DU MOTEUR DE RÈGLES, PLUS DE LA CONFIGURATION.
            var bareme = await _commissions.ComputeCommissionAsync(
                line.SellerId, CategorieInconnue, baseVendeur, order.Currency, cancellationToken);

            // Le moteur arrondit au centime ; le grand livre ne porte que des
            // francs CFA, qui n'ont pas de subdivision en usage.
            var commission = Math.Round(bareme.CommissionAmount);
            var providerFee = Math.Round(gross * provRate / facteurAffichage);

            var earning = SellerEarning.Create(
                order.Id, line.OfferId, line.SellerId, line.ProductId, gross, commission, providerFee, order.Currency);

            if (earning.IsSuccess)
            {
                await _earningRepository.AddAsync(earning.Value, cancellationToken);

                netBySeller[line.SellerId] = netBySeller.GetValueOrDefault(line.SellerId) + earning.Value.NetAmount;
                totalCommission += earning.Value.CommissionAmount;
                totalProvider += earning.Value.ProviderFeeAmount;
                brutComptabilise += earning.Value.GrossAmount;
            }
        }

        // UNE SEULE OPÉRATION COMPTABLE, ET ELLE DOIT S'ÉQUILIBRER (§10.13,
        // ISSUE-051).
        var operation = _wallets.Ouvrir();

        _wallets.ContrepartieExterne(
            operation, WalletDirection.Debit, brutComptabilise + order.ShippingFee,
            order.Currency, "order_confirmed", "order", order.Id);

        // Portefeuille vendeur : le net va au solde à venir (libéré à la
        // livraison).
        foreach (var (sellerId, net) in netBySeller)
        {
            await _wallets.CreditSellerPendingAsync(sellerId, net, order.Currency, order.Id, cancellationToken, operation);
        }

        // Portefeuille plateforme : commission + frais provider + frais de
        // livraison encaissés.
        await _wallets.CreditPlatformCommissionAsync(totalCommission, order.Currency, order.Id, cancellationToken, operation);
        await _wallets.CreditPlatformProviderFeeAsync(totalProvider, order.Currency, order.Id, cancellationToken, operation);
        await _wallets.CreditPlatformShippingAsync(order.ShippingFee, order.Currency, order.Id, cancellationToken, operation);

        var equilibre = await _wallets.CloreAsync(operation, cancellationToken);
        if (equilibre.IsFailure)
        {
            _logger.LogCritical(
                "Commande {OrderId} : la comptabilisation NE S'ÉQUILIBRE PAS — {Code} : {Message}. "
                + "Brut comptabilisé {Brut} + port {Port} face à net vendeur {Net}, commission {Commission} "
                + "et frais {Frais}. RIEN n'est écrit : ni gain, ni solde, ni grand livre.",
                order.Id, equilibre.Error.Code, equilibre.Error.Message,
                brutComptabilise, order.ShippingFee, netBySeller.Values.Sum(), totalCommission, totalProvider);

            throw new InvalidOperationException(
                $"Comptabilisation de la commande {order.Id} déséquilibrée : {equilibre.Error.Message}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>LE RESTAURANT EST CRÉDITÉ COMME UN VENDEUR, PARCE QU'IL EN EST UN.</summary>
    private async Task AccruerRestaurationAsync(
        OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RestaurantId is not { } restaurantId)
        {
            // `Order.Create` l'interdit.
            _logger.LogError(
                "Commande de repas {OrderId} confirmée SANS restaurant : aucun gain n'est comptabilisé.",
                integrationEvent.OrderId);

            return;
        }

        if (await _earningRepository.ExistsForOrderAsync(integrationEvent.OrderId, cancellationToken))
        {
            return;
        }

        var restaurant = await _food.GetRestaurantAsync(restaurantId, cancellationToken);

        if (restaurant?.PayoutSellerId is not { } beneficiaire)
        {
            // ON JOURNALISE EN ERREUR ET ON N'ÉCRIT RIEN.
            _logger.LogError(
                "Commande {OrderId} : le restaurant {RestaurantId} n'a AUCUN dossier de reversement. "
                + "La recette est encaissée et rien ne peut être versé au restaurateur.",
                integrationEvent.OrderId, restaurantId);

            throw new InvalidOperationException(
                $"Restaurant {restaurantId} sans dossier de reversement : commande {integrationEvent.OrderId} non comptabilisée.");
        }

        var order = await _orderingModuleApi.GetOrderAsync(integrationEvent.OrderId, cancellationToken);

        // Même partage qu'en marchandise, et pour la même raison : une commande
        // introuvable est un incident passager qui mérite un rejeu ; une commande
        // vide n'a rien à comptabiliser et le rejeu n'y changerait rien.
        if (order is null)
        {
            _logger.LogError(
                "Commande de repas {OrderId} confirmée mais INTROUVABLE : aucun gain comptabilisé, rejeu demandé.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande {integrationEvent.OrderId} introuvable : comptabilisation des gains impossible.");
        }

        if (order.Lines.Count == 0)
        {
            _logger.LogWarning(
                "Commande de repas {OrderId} confirmée SANS ligne : rien à comptabiliser.", integrationEvent.OrderId);

            return;
        }

        // LA RESTAURATION NE PASSE PAS PAR LE MOTEUR DE RÈGLES, ET C'EST VOULU.

        // LA COMMISSION SE PRÉLÈVE SUR LE PRIX, PAS SUR UN PRIX NET RECONSTRUIT.
        var commRate = _pricing.FoodCommissionRate;
        var provRate = _pricing.ProviderFeeRate;

        // Le brut du restaurant : ce que l'acheteur a payé pour les PLATS. Les
        // frais de livraison n'en font pas partie — ils rémunèrent la course, pas
        // la cuisine — et sont suivis séparément.
        var brut = order.Lines
            .Where(l => string.Equals(l.Kind, "Food", StringComparison.Ordinal))
            .Sum(l => Math.Max(0m, (l.UnitBasePrice - l.SellerDiscount) * l.Quantity));

        var commission = Math.Round(brut * commRate);
        var fraisProvider = Math.Round(brut * provRate);

        // LE NET NE PEUT PAS DEVENIR NÉGATIF.
        if (commission + fraisProvider > brut)
        {
            // Même raison : un abandon silencieux perdrait définitivement la
            // comptabilisation d'une commande encaissée.
            _logger.LogError(
                "Commande {OrderId} : commission ({Comm}) + frais ({Prov}) dépassent le prix des plats "
                + "({Brut}). Corrigez « Pricing:FoodCommissionRate ».",
                integrationEvent.OrderId, commission, fraisProvider, brut);

            throw new InvalidOperationException(
                $"Taux de commission restauration aberrant : commande {integrationEvent.OrderId} non comptabilisée.");
        }

        // `OfferId` ET `ProductId` PORTENT L'IDENTIFIANT DU RESTAURANT.
        var gain = SellerEarning.Create(
            order.Id, restaurantId, beneficiaire, restaurantId,
            brut, commission, fraisProvider, order.Currency);

        if (gain.IsFailure)
        {
            _logger.LogError(
                "Commande {OrderId} : gain restaurant NON comptabilisé — {Code} : {Message}.",
                integrationEvent.OrderId, gain.Error.Code, gain.Error.Message);

            return;
        }

        await _earningRepository.AddAsync(gain.Value, cancellationToken);

        // Même opération comptable que pour la marchandise, et pour la même raison
        // (§10.13, ISSUE-051).
        var operation = _wallets.Ouvrir();

        _wallets.ContrepartieExterne(
            operation, WalletDirection.Debit, brut + order.ShippingFee,
            order.Currency, "order_confirmed", "order", order.Id);

        // Le net va au solde À VENIR : il ne devient payable qu'à la livraison,
        // exactement comme pour un vendeur.
        await _wallets.CreditSellerPendingAsync(
            beneficiaire, gain.Value.NetAmount, order.Currency, order.Id, cancellationToken, operation);

        await _wallets.CreditPlatformCommissionAsync(
            gain.Value.CommissionAmount, order.Currency, order.Id, cancellationToken, operation);
        await _wallets.CreditPlatformProviderFeeAsync(
            gain.Value.ProviderFeeAmount, order.Currency, order.Id, cancellationToken, operation);
        await _wallets.CreditPlatformShippingAsync(
            order.ShippingFee, order.Currency, order.Id, cancellationToken, operation);

        var equilibre = await _wallets.CloreAsync(operation, cancellationToken);
        if (equilibre.IsFailure)
        {
            _logger.LogCritical(
                "Commande de repas {OrderId} : la comptabilisation NE S'ÉQUILIBRE PAS — {Code} : {Message}. "
                + "Brut {Brut} + port {Port} face à net {Net}, commission {Commission} et frais {Frais}. "
                + "RIEN n'est écrit.",
                order.Id, equilibre.Error.Code, equilibre.Error.Message,
                brut, order.ShippingFee, gain.Value.NetAmount,
                gain.Value.CommissionAmount, gain.Value.ProviderFeeAmount);

            throw new InvalidOperationException(
                $"Comptabilisation de la commande de repas {order.Id} déséquilibrée : {equilibre.Error.Message}");
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
