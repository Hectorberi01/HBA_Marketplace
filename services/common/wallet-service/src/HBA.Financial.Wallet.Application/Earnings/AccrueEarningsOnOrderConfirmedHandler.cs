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

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// À la confirmation d'une commande, comptabilise le gain de chaque ligne pour
/// son vendeur. Le brut d'une ligne (prix produit payé par l'acheteur) = prix
/// vendeur net × (1 + commission + provider). On retrouve donc le prix vendeur
/// net en divisant par ce facteur, le MOTEUR DE RÈGLES dit quelle commission s'y
/// applique, les frais provider restent une fraction de ce même net, et le net
/// vendeur est le RESTE (brut − commission − provider), ce qui garantit une
/// somme exacte. Idempotent par commande.
///
/// Alimente aussi les portefeuilles : le NET va au « solde à venir » de chaque
/// vendeur, la commission au solde commission de la plateforme, les frais
/// provider au solde provider de la plateforme, et les frais de livraison au
/// solde livraison de la plateforme.
///
/// LE TAUX EST FIGÉ ICI, ET NE SE RELIT JAMAIS.
///
/// <c>SellerEarning</c> conserve <c>GrossAmount</c>, <c>CommissionAmount</c>,
/// <c>ProviderFeeAmount</c> et <c>NetAmount</c> — des MONTANTS, pas un taux. Un
/// gain déjà comptabilisé ne bouge donc pas parce qu'un admin a modifié une
/// règle depuis, et la contre-passation d'un retour relit ces montants plutôt
/// que de recalculer (voir ReverseEarningsOnReturnRefundedHandler).
/// </summary>
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CATÉGORIE N'EST PAS CONNUE ICI : LES RÈGLES « CATÉGORIE » SONT INERTES.
    ///
    /// <c>OrderLineSummary</c> porte l'offre, le produit et le vendeur — pas la
    /// catégorie. La retrouver imposerait un appel à catalog-service PAR LIGNE,
    /// c'est-à-dire un saut réseau de plus dans le chemin de l'argent, pour une
    /// portée que personne n'utilise aujourd'hui.
    ///
    /// À dire tout haut plutôt qu'à taire : une règle « catégorie X à 12 % » se
    /// crée, s'affiche, et ne s'applique PAS à la comptabilisation. Les règles
    /// « vendeur » et « globale », elles, s'appliquent. Le jour où la ligne de
    /// commande portera sa catégorie, c'est la seule valeur à changer ici.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static readonly Guid CategorieInconnue = Guid.Empty;

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // ═════════════════════════════════════════════════════════════════════
        // UNE COMMANDE DE REPAS SE CRÉDITE, MAIS PAS AU MÊME BÉNÉFICIAIRE NI
        // AU MÊME TAUX.
        //
        // Ce gestionnaire ne lit PAS `SellerShares` : il relit les lignes de la
        // commande, dont le `SellerId` est vide pour un plat. Sans traitement
        // séparé, chaque plat produisait un gain au nom du vendeur
        // « 00000000-… » et un crédit sur un portefeuille fantôme.
        //
        // Le bénéficiaire est le DOSSIER VENDEUR DU RESTAURANT — c'est lui qui
        // porte le compte Mobile Money, et c'est par lui que passent portefeuille,
        // retrait et payout. Le taux, lui, est propre à la restauration.
        // ═════════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // CET ABANDON ÉTAIT SILENCIEUX, SUR UNE COMMANDE DÉJÀ ENCAISSÉE.
        //
        // Un simple `return` marquait le message traité : ni gain vendeur, ni
        // commission, ni frais de livraison n'étaient jamais comptabilisés, et
        // plus aucune tentative n'avait lieu. La perte était définitive, et rien
        // ne la signalait — pas même une ligne de journal.
        //
        // Les deux cas ne se traitent pas pareil :
        //   • commande INTROUVABLE — order-service est momentanément muet, ou la
        //     réplication a du retard. On relance : l'outbox rejouera, et
        //     l'accrual est idempotent (`ExistsForOrderAsync`). Une lettre morte
        //     finit par se voir ; un `return` non ;
        //   • commande SANS LIGNE — il n'y a rien à comptabiliser, et le rejeu
        //     n'y changera rien. On journalise et on s'arrête.
        // ═════════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // LE DIVISEUR RESTE LE BARÈME D'AFFICHAGE, PAS LE TAUX DU MOTEUR.
        //
        // Le prix acheteur a été CONSTRUIT comme `net × (1 + commission +
        // provider)` avec les taux de `PlatformPricing` — c'est ce facteur-là,
        // et lui seul, qui permet de retrouver le prix vendeur net. Diviser par
        // le taux négocié d'un vendeur inverserait une majoration qui n'a jamais
        // été appliquée, et le brut cesserait de se refermer.
        // ═════════════════════════════════════════════════════════════════════
        var facteurAffichage = 1 + _pricing.PlatformCommissionRate + provRate;

        var netBySeller = new Dictionary<Guid, decimal>();
        var totalCommission = 0m;
        var totalProvider = 0m;

        // Le BRUT effectivement comptabilisé, accumulé indépendamment de sa
        // répartition. C'est lui qui sert de contrepartie externe plus bas, et
        // c'est cette indépendance qui donne un sens au contrôle d'équilibre :
        // si la répartition en net + commission + frais cesse d'être exhaustive,
        // les deux membres cessent de coïncider et l'opération est refusée.
        var brutComptabilise = 0m;

        foreach (var line in order.Lines)
        {
            var gross = (line.UnitBasePrice - line.SellerDiscount) * line.Quantity;
            if (gross < 0m)
            {
                gross = 0m;
            }

            // Le prix vendeur net : la base sur laquelle la commission se
            // calcule, et donc celle que le moteur doit recevoir.
            var baseVendeur = gross / facteurAffichage;

            // ═════════════════════════════════════════════════════════════════
            // LE TAUX VIENT DU MOTEUR DE RÈGLES, PLUS DE LA CONFIGURATION.
            //
            // Un admin créait une règle « vendeur X à 5 % », la voyait dans son
            // écran d'administration — et la plateforme prélevait quand même les
            // 10 % de `Pricing:PlatformCommissionRate`. `ICommissionModuleApi`
            // n'avait aucun appelant hors de Billing : l'écran administrait une
            // table que l'argent ne lisait pas.
            //
            // Billing et Settlement vivent dans le MÊME service et le MÊME scope
            // DI (voir BillingModuleInstaller et WalletModuleInstaller) : appel
            // en processus, aucun réseau, aucun gRPC à ajouter.
            //
            // UN MOTEUR INJOIGNABLE DOIT FAIRE ÉCHOUER, PAS DÉGRADER.
            //
            // Aucun `catch` ici, délibérément. Retomber sur le taux par défaut
            // prélèverait 10 % à un vendeur qui en a négocié 5, et ce montant
            // serait FIGÉ dans le grand livre : l'erreur deviendrait
            // indétectable après coup. L'exception remonte, l'outbox rejoue,
            // l'idempotence garantit qu'un rejeu ne double rien. Une commande
            // non comptabilisée se voit ; une commission fausse, non.
            // ═════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // UNE SEULE OPÉRATION COMPTABLE, ET ELLE DOIT S'ÉQUILIBRER (§10.13, ISSUE-051).
        //
        // L'INVARIANT EXISTAIT, TESTÉ, ET N'ÉTAIT APPELÉ NULLE PART.
        //
        // Ces quatre crédits étaient quatre opérations indépendantes, sans
        // contrepartie : l'argent apparaissait dans les comptes sans venir de nulle
        // part. Rien ne vérifiait que la répartition d'une commande — net vendeur,
        // commission, frais provider — épuisait le brut encaissé. Une composante
        // oubliée ou un arrondi qui dérive ne se serait vu qu'au jour où quelqu'un
        // aurait additionné les mouvements, des mois plus tard, sans plus pouvoir
        // dire QUELLE écriture manquait.
        //
        // POURQUOI CE CONTRÔLE-CI N'EST PAS TAUTOLOGIQUE.
        //
        // `brutComptabilise` est accumulé dans la boucle, gain par gain, depuis
        // `earning.GrossAmount`. Les crédits, eux, viennent de `NetAmount`,
        // `CommissionAmount` et `ProviderFeeAmount`, calculés séparément. Les deux
        // membres ne partagent aucune variable : leur égalité est une PROPRIÉTÉ,
        // pas une écriture recopiée.
        //
        // Les frais de port entrent des deux côtés parce qu'ils sont encaissés en
        // plus du prix des articles : ils augmentent le brut reçu et le solde
        // « livraison » de la plateforme, du même montant.
        //
        // UN REFUS FAIT ÉCHOUER LA COMMANDE ENTIÈRE, DÉLIBÉRÉMENT.
        //
        // C'est la position déjà tenue plus haut pour le moteur de commission :
        // « une commande non comptabilisée se voit ; une commission fausse, non ».
        // L'exception remonte, l'outbox rejoue, et le message finit en lettre morte
        // après trois tentatives — ce qui est exactement le sort d'un message qu'on
        // ne sait pas traiter.
        // ═════════════════════════════════════════════════════════════════════
        var operation = _wallets.Ouvrir();

        _wallets.ContrepartieExterne(
            operation, WalletDirection.Debit, brutComptabilise + order.ShippingFee,
            order.Currency, "order_confirmed", "order", order.Id);

        // Portefeuille vendeur : le net va au solde à venir (libéré à la livraison).
        foreach (var (sellerId, net) in netBySeller)
        {
            await _wallets.CreditSellerPendingAsync(sellerId, net, order.Currency, order.Id, cancellationToken, operation);
        }

        // Portefeuille plateforme : commission + frais provider + frais de livraison encaissés.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RESTAURANT EST CRÉDITÉ COMME UN VENDEUR, PARCE QU'IL EN EST UN.
    ///
    /// POURQUOI RÉUTILISER `SellerEarning` PLUTÔT QUE CRÉER UN GRAND LIVRE.
    ///
    /// Toute la chaîne d'aval — solde à venir, libération à la livraison, demande
    /// de retrait, lot de reversement, payout Mobile Money, relevés — est indexée
    /// sur un identifiant de vendeur et résout le compte de destination par le
    /// dossier. Un grand livre parallèle aurait imposé de recopier cette chaîne
    /// entière, et de la tenir d'accord à chaque évolution.
    ///
    /// `Restaurant.PayoutSellerId` désigne ce dossier. Il est obligatoire pour
    /// mettre un établissement en service, précisément pour que ce chemin existe.
    ///
    /// CE QUI DIFFÈRE VRAIMENT : LE TAUX ET L'ABSENCE DE LIGNE PAR OFFRE.
    ///
    /// La commission de restauration est un réglage distinct. Et un plat n'a ni
    /// offre ni produit : le gain est enregistré une fois pour la commande, avec
    /// l'identifiant du restaurant en guise de référence d'article — c'est ce que
    /// le relevé du restaurateur affichera.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task AccruerRestaurationAsync(
        OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RestaurantId is not { } restaurantId)
        {
            // `Order.Create` l'interdit. Si cela survient, la commande est payée et
            // personne ne sait qui payer : mieux vaut ne rien créditer que de
            // créditer au hasard.
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
            //
            // Un établissement en service DOIT avoir un dossier de reversement —
            // `Restaurant.Submit` l'exige. S'il n'en a pas, c'est un dossier
            // détaché après coup, ou un établissement validé avant cette règle :
            // dans les deux cas la recette est encaissée et le restaurateur n'a
            // aucun moyen d'être payé. Inventer un bénéficiaire serait pire.
            // ON RELANCE PLUTÔT QUE D'ABANDONNER.
            //
            // Un `return` marquait le message traité : plus AUCUNE tentative, et
            // ni le gain, ni la commission, ni les frais de livraison n'étaient
            // jamais comptabilisés — sur une commande pourtant encaissée. La perte
            // était définitive et silencieuse.
            //
            // Rattacher un dossier de reversement prend quelques minutes ; laisser
            // le message en souffrance donne cette fenêtre, puis une lettre morte
            // qui, elle, se voit.
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

        // ═════════════════════════════════════════════════════════════════════
        // LA RESTAURATION NE PASSE PAS PAR LE MOTEUR DE RÈGLES, ET C'EST VOULU.
        //
        // La marchandise interroge désormais `ICommissionModuleApi`. Un plat non,
        // pour deux raisons qui tiennent au moteur lui-même :
        //
        //   • ses portées sont Global / Catégorie / Vendeur, et la « catégorie »
        //     y désigne une catégorie de CATALOGUE. Une ligne de repas porte un
        //     restaurant et un plat, jamais de catégorie catalogue : il faudrait
        //     inventer un identifiant « restauration » et le résoudre contre une
        //     table à laquelle il n'appartient pas ;
        //   • une règle GLOBALE capterait alors les repas au passage, en silence.
        //     Le premier ajustement de la commission marchandise changerait la
        //     rémunération de tous les restaurateurs sans que personne l'ait
        //     décidé — exactement ce que la séparation des deux taux évite.
        //
        // S'ajoute la base de calcul, qui n'est pas la même (voir juste dessous).
        // `Pricing:FoodCommissionRate` reste donc le réglage de la restauration.
        // Le jour où une commission par ÉTABLISSEMENT sera décidée, c'est une
        // portée « Restaurant » qu'il faudra ajouter au moteur, pas un détournement
        // de la portée « Catégorie ».
        // ═════════════════════════════════════════════════════════════════════

        // ═════════════════════════════════════════════════════════════════════
        // LA COMMISSION SE PRÉLÈVE SUR LE PRIX, PAS SUR UN PRIX NET RECONSTRUIT.
        //
        // La marchandise applique `brut × taux / (1 + taux + provider)`, et c'est
        // juste POUR ELLE : le prix acheteur y est CONSTRUIT comme une majoration
        // du prix net vendeur, donc `brut = net × (1 + taux + provider)`. La
        // division inverse la majoration.
        //
        // Un plat n'est pas construit ainsi. Le restaurateur affiche un prix de
        // carte, et c'est ce prix que l'acheteur paie — aucune majoration n'a été
        // appliquée. Reprendre la formule marchandise prélèverait donc
        // `0,10 / 1,15 ≈ 8,7 %` là où le réglage annonce 10 %, et le taux réel
        // dépendrait des frais du prestataire de paiement. Un réglage qui ne
        // prélève pas ce qu'il annonce est pire qu'un mauvais réglage : personne
        // ne s'en aperçoit avant le rapprochement comptable.
        //
        // Sur un repas, la plateforme et le prestataire se servent DANS le prix
        // affiché, et le restaurateur touche le reste.
        // ═════════════════════════════════════════════════════════════════════
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
        //
        // `commRate + provRate` est une configuration : rien n'empêche d'y écrire
        // 1,20. Sans ce garde-fou, le restaurateur devrait de l'argent pour avoir
        // servi un repas, et `SellerEarning` l'enregistrerait sans broncher.
        if (commission + fraisProvider > brut)
        {
            // Même raison : un abandon silencieux perdrait définitivement la
            // comptabilisation d'une commande encaissée. Une configuration
            // aberrante se corrige, et le rejeu reprendra.
            _logger.LogError(
                "Commande {OrderId} : commission ({Comm}) + frais ({Prov}) dépassent le prix des plats "
                + "({Brut}). Corrigez « Pricing:FoodCommissionRate ».",
                integrationEvent.OrderId, commission, fraisProvider, brut);

            throw new InvalidOperationException(
                $"Taux de commission restauration aberrant : commande {integrationEvent.OrderId} non comptabilisée.");
        }

        // `OfferId` ET `ProductId` PORTENT L'IDENTIFIANT DU RESTAURANT.
        //
        // Un plat n'a ni offre ni produit. Les laisser vides rendrait le relevé du
        // restaurateur illisible — des lignes sans rattachement — et empêcherait de
        // retrouver la recette d'un établissement. Le nom des colonnes ment un peu ;
        // les laisser vides mentirait davantage.
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
        // (§10.13, ISSUE-051). La contrepartie est le brut du TICKET, calculé plus
        // haut à partir du prix de carte — indépendamment de sa répartition en net,
        // commission et frais. Ajouter les frais de port des deux côtés : ils sont
        // encaissés en plus des plats.
        var operation = _wallets.Ouvrir();

        _wallets.ContrepartieExterne(
            operation, WalletDirection.Debit, brut + order.ShippingFee,
            order.Currency, "order_confirmed", "order", order.Id);

        // Le net va au solde À VENIR : il ne devient payable qu'à la livraison,
        // exactement comme pour un vendeur. C'est ce qui protège la plateforme
        // d'un versement sur une commande qui sera remboursée.
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
