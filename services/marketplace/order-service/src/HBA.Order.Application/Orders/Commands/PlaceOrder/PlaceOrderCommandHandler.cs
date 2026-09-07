using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Contracts;
using HBA.DeliveryPricing.Contracts;
using HBA.Inventory.Contracts;
using HBA.Products.Contracts;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;
using Microsoft.Extensions.Logging;
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Application.Orders.Commands.PlaceOrder;

/// <summary>Orchestrateur du Saga de commande.</summary>
internal sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    /// <summary>
    /// Tolérance de comparaison entre le point de chute FIGÉ dans le devis et celui
    /// de l'adresse enregistrée sur la commande.
    /// </summary>
    private const double ToleranceDegres = 0.0005;

    private readonly ICartModuleApi _cartModuleApi;
    private readonly IProductsModuleApi _catalogue;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    /// <summary>La relecture du devis de course.</summary>
    private readonly IDeliveryQuoteLookup _devis;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly ILogger<PlaceOrderCommandHandler> _logger;

    public PlaceOrderCommandHandler(
        ICartModuleApi cartModuleApi,
        IProductsModuleApi catalogue,
        IInventoryModuleApi inventoryModuleApi,
        IDeliveryQuoteLookup devis,
        IOrderRepository orderRepository,
        IOrderingUnitOfWork unitOfWork,
        ILogger<PlaceOrderCommandHandler> logger)
    {
        _cartModuleApi = cartModuleApi;
        _catalogue = catalogue;
        _inventoryModuleApi = inventoryModuleApi;
        _devis = devis;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var cart = await _cartModuleApi.GetActiveCartAsync(command.BuyerId, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict("ordering.cart_empty", "Aucun panier valorisé à commander."));
        }

        // IDEMPOTENCE : LE MÊME PANIER NE PRODUIT QU'UNE COMMANDE.
        var deja = await _orderRepository.GetByCartAsync(cart.CartId, cancellationToken);
        if (deja is not null)
        {
            _logger.LogInformation(
                "Panier {CartId} déjà passé en commande {OrderId} : la requête est rejouée, "
                + "aucune seconde commande n'est créée.",
                cart.CartId, deja.Id.Value);

            return deja.Id.Value;
        }

        // LA NATURE VOYAGE AVEC LA LIGNE, ET UNE NATURE INCONNUE EST UN REFUS.
        var naturesInconnues = cart.Lines
            .Select(l => l.Kind)
            .Where(k => !Enum.TryParse<OrderLineKind>(k, ignoreCase: false, out _))
            .Distinct()
            .ToList();

        if (naturesInconnues.Count > 0)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "ordering.unknown_line_kind",
                $"Nature de ligne inconnue : {string.Join(", ", naturesInconnues)}."));
        }

        // LE CATALOGUE EST RELU ICI, ET IL NE L'ÉTAIT NULLE PART (ISSUE-048).
        var lignesCatalogue = cart.Lines
            .Where(l => Enum.Parse<OrderLineKind>(l.Kind) == OrderLineKind.Goods)
            .ToList();

        if (lignesCatalogue.Count > 0)
        {
            var offres = await _catalogue.GetOffersAsync(
                lignesCatalogue.Select(l => l.OfferId).Distinct().ToList(), cancellationToken);

            foreach (var ligne in lignesCatalogue)
            {
                if (!offres.TryGetValue(ligne.OfferId, out var offre))
                {
                    // Offre archivée, produit supprimé, ou catalogue qui ne la
                    // connaît plus.
                    return Result.Failure<Guid>(Error.Conflict(
                        "ordering.offer_unavailable",
                        $"L'article « {ligne.Sku} » n'est plus proposé à la vente."));
                }

                if (!offre.IsPurchasable)
                {
                    return Result.Failure<Guid>(Error.Conflict(
                        "ordering.offer_not_purchasable",
                        $"L'article « {ligne.Sku} » n'est plus disponible à la commande."));
                }

                // `EffectivePrice` — prix promotionnel s'il court, prix courant
                // sinon.
                if (offre.EffectivePrice != ligne.UnitBaseAmount)
                {
                    _logger.LogInformation(
                        "Checkout refusé pour l'acheteur {BuyerId} : le prix de l'offre {OfferId} "
                        + "({Sku}) est passé de {Ancien} à {Nouveau} {Devise} depuis l'ajout au panier.",
                        command.BuyerId, ligne.OfferId, ligne.Sku,
                        ligne.UnitBaseAmount, offre.EffectivePrice, offre.Currency);

                    return Result.Failure<Guid>(Error.Conflict(
                        "ordering.price_changed",
                        $"Le prix de « {ligne.Sku} » a changé depuis son ajout au panier. "
                        + "Vérifiez votre panier avant de commander."));
                }
            }
        }

        var drafts = cart.Lines.Select(l => new OrderLineDraft(
            l.OfferId, l.ProductId, l.SellerId, l.Sku, l.ShipFromLocationId, l.Quantity,
            l.UnitBaseAmount, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice,
            Enum.Parse<OrderLineKind>(l.Kind),
            l.RestaurantId,
            l.MenuItemId,
            l.Notes,
            l.Options?.Select(o => new OrderLineOptionDraft(o.OptionGroupId, o.OptionId)).ToList()));

        // Le code promo du panier est FIGÉ dans la commande.
        var orderResult = OrderAggregate.Create(command.BuyerId, cart.CartId, cart.Currency, drafts, cart.PromotionCode);
        if (orderResult.IsFailure)
        {
            return Result.Failure<Guid>(orderResult.Error);
        }

        var order = orderResult.Value;

        // L'ADRESSE DE LIVRAISON EST UN INVARIANT DE LA COMMANDE, PAS UNE
        // VÉRIFICATION D'INTERFACE.
        if (command.ShippingAddress is not { } addr
            || string.IsNullOrWhiteSpace(addr.CommuneCode)
            || string.IsNullOrWhiteSpace(addr.Landmark))
        {
            return Result.Failure<Guid>(Error.Validation(
                "ordering.shipping_address_required",
                "Une adresse de livraison complète est obligatoire : commune et point de repère."));
        }

        // POUR UN REPAS, LA COMMUNE ET LE REPÈRE NE SUFFISENT PAS.
        if (order.Kind == OrderLineKind.Food
            && (command.ShippingAddress is not { } food
                || food.Latitude is null
                || food.Longitude is null
                || string.IsNullOrWhiteSpace(food.Phone)))
        {
            return Result.Failure<Guid>(Error.Validation(
                "ordering.food_address_incomplete",
                "Une commande de repas exige une position sur la carte et un téléphone joignable."));
        }

        order.SetShippingAddress(
            addr.Label, addr.Recipient, addr.Phone,
            addr.CommuneCode, addr.Quartier, addr.Landmark, addr.Line1, addr.CountryCode,
            addr.Latitude, addr.Longitude);

        // LE SERVEUR FIXE LES FRAIS DE LIVRAISON. L'ACHETEUR NE FAIT QUE DÉSIGNER
        // LE DEVIS QU'IL A VU.
        var frais = await ResoudreFraisDeLivraisonAsync(order, addr, command, cancellationToken);
        if (frais.IsFailure)
        {
            return Result.Failure<Guid>(frais.Error);
        }

        order.SetShippingFee(frais.Value.Montant, frais.Value.QuoteId);

        await _orderRepository.AddAsync(order, cancellationToken);

        // ÉTAPE SAGA : RÉSERVATION DU STOCK, AVEC COMPENSATION EN CAS D'ÉCHEC.
        var aReserver = order.Lines
            .Where(l => l.RequiresStockReservation)
            .GroupBy(l => (l.Sku, l.ShipFromLocationId))
            .Select(g => (Sku: g.Key.Sku, LocationId: g.Key.ShipFromLocationId, Quantity: g.Sum(l => l.Quantity)))
            .ToList();

        var reserved = new List<(string Sku, Guid LocationId)>();
        foreach (var demande in aReserver)
        {
            var ok = await _inventoryModuleApi.TryReserveAsync(
                demande.Sku, demande.LocationId, order.Id.Value, demande.Quantity, cancellationToken);

            if (ok)
            {
                reserved.Add((demande.Sku, demande.LocationId));
                continue;
            }

            // Compensation : on libère les réservations déjà obtenues.
            foreach (var r in reserved)
            {
                await _inventoryModuleApi.ReleaseReservationAsync(r.Sku, r.LocationId, order.Id.Value, cancellationToken);
            }

            order.Fail($"Stock indisponible pour le SKU {demande.Sku}.");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<Guid>(Error.Conflict("ordering.out_of_stock", $"Stock insuffisant pour {demande.Sku}."));
        }

        order.MarkAwaitingPayment();

        // SI LA PERSISTANCE ÉCHOUE ICI, LE STOCK EST PERDU SANS VENTE (ISSUE-032).
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Commande {OrderId} : la persistance a échoué APRÈS {Count} réservation(s) de stock. "
                + "Libération en compensation ; sans elle, ce stock resterait immobilisé pour une "
                + "commande qui n'existe pas.",
                order.Id.Value, reserved.Count);

            foreach (var r in reserved)
            {
                try
                {
                    await _inventoryModuleApi.ReleaseReservationAsync(
                        r.Sku, r.LocationId, order.Id.Value, CancellationToken.None);
                }
                catch (Exception echecLiberation)
                {
                    _logger.LogCritical(
                        echecLiberation,
                        "Commande {OrderId} : la libération du SKU {Sku} sur l'emplacement {LocationId} a "
                        + "ÉCHOUÉ. Ce stock reste réservé pour une commande inexistante — libération "
                        + "manuelle requise.",
                        order.Id.Value, r.Sku, r.LocationId);
                }
            }

            throw;
        }

        return order.Id.Value;
    }

    /// <summary>Les frais retenus, et le devis qui les fonde.</summary>
    private readonly record struct FraisDeLivraison(decimal Montant, string? QuoteId);

    /// <summary>DÉTERMINE LES FRAIS DE LIVRAISON — SANS JAMAIS CROIRE L'ACHETEUR.</summary>
    private async Task<Result<FraisDeLivraison>> ResoudreFraisDeLivraisonAsync(
        OrderAggregate order,
        ShippingAddressInput addr,
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeliveryQuoteId))
        {
            // UN REPAS SANS DEVIS EST REFUSÉ.
            if (order.Kind == OrderLineKind.Food)
            {
                return Result.Failure<FraisDeLivraison>(Error.Validation(
                    "ordering.delivery_quote_required",
                    "Les frais de livraison d'un repas doivent être chiffrés par un devis "
                    + "avant le paiement. Demandez un devis de course, puis reprenez le "
                    + "paiement avec son identifiant."));
            }

            _logger.LogWarning(
                "Commande marchandise de l'acheteur {BuyerId} enregistrée SANS devis de course : "
                + "frais de livraison à zéro. La course sera achetée au prix réel et la "
                + "plateforme en supportera le coût.",
                command.BuyerId);

            return new FraisDeLivraison(0m, null);
        }

        var devis = await _devis.LookupQuoteAsync(command.DeliveryQuoteId, cancellationToken);

        // INTROUVABLE OU ILLISIBLE : MÊME REFUS, MÊME GESTE ATTENDU.
        if (devis is null)
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_not_found",
                "Ce devis de livraison est introuvable. Demandez un nouveau prix "
                + "avant de valider la commande."));
        }

        // CONSOMMÉ AVANT EXPIRÉ : L'ORDRE DES DEUX CONTRÔLES CHANGE LE MESSAGE.
        if (devis.IsConsumed)
        {
            return Result.Failure<FraisDeLivraison>(Error.Conflict(
                "ordering.delivery_quote_used",
                "Ce devis de livraison a déjà servi à une course. Demandez un nouveau prix."));
        }

        if (devis.IsExpired)
        {
            return Result.Failure<FraisDeLivraison>(Error.Conflict(
                "ordering.delivery_quote_expired",
                $"Ce devis de livraison a expiré le {devis.ExpiresAtUtc:u}. "
                + "Demandez un nouveau prix avant de valider la commande."));
        }

        // UN DEVIS DE PARTENAIRE N'EST PAS UN DEVIS D'ACHETEUR.
        if (devis.PartnerId is not null)
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_foreign",
                "Ce devis de livraison n'a pas été établi pour cette commande."));
        }

        // LE DEVIS DOIT AVOIR ÉTÉ ÉTABLI POUR CETTE ADRESSE-CI.
        if (addr.Latitude is { } lat && addr.Longitude is { } lon
            && (Math.Abs(devis.DropoffLatitude - lat) > ToleranceDegres
                || Math.Abs(devis.DropoffLongitude - lon) > ToleranceDegres))
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_address_mismatch",
                "Ce devis de livraison a été établi pour une autre adresse. "
                + "Demandez un nouveau prix pour l'adresse choisie."));
        }

        // LE NIVEAU DE SERVICE DU DEVIS DOIT ÊTRE CELUI QUI SERA ACHETÉ.
        var typeAttendu = order.Kind == OrderLineKind.Food ? "Express" : "Standard";

        if (!string.Equals(devis.DeliveryType, typeAttendu, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<FraisDeLivraison>(Error.Validation(
                "ordering.delivery_quote_wrong_service",
                $"Ce devis de livraison a été établi pour un service « {devis.DeliveryType} », "
                + $"alors que cette commande sera livrée en « {typeAttendu} ». "
                + "Demandez un nouveau prix."));
        }

        // C'EST LE MONTANT DU DEVIS QUI ENTRE DANS LA COMMANDE — PAS UN MONTANT
        // COMPARÉ AU DEVIS.
        return new FraisDeLivraison(devis.Total, devis.QuoteId);
    }
}
