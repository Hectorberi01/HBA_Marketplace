using HBA.FoodOrders.Domain.Orders.Events;
using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.FoodOrders.Domain.Orders;

/// <summary>
/// Commande de repas : les plats d'UN restaurant, leur prix figé, l'adresse de
/// livraison et le devis de course.
/// </summary>
public sealed class MealOrder : AggregateRoot<MealOrderId>
{
    private readonly List<MealOrderLine> _lines = new();

    private MealOrder()
    {
    }

    private MealOrder(
        MealOrderId id, Guid buyerId, Guid restaurantId, Guid cartId, string currency, string? promotionCode)
        : base(id)
    {
        BuyerId = buyerId;
        RestaurantId = restaurantId;
        CartId = cartId;
        Currency = currency;
        PromotionCode = promotionCode;
        Status = MealOrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BuyerId { get; private set; }

    /// <summary>L'établissement qui prépare cette commande.</summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>Le panier dont cette commande est née.</summary>
    public Guid CartId { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>Code promo du panier, FIGÉ au moment de la commande.</summary>
    public string? PromotionCode { get; private set; }

    public MealOrderStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal TotalSellerDiscount { get; private set; }

    public decimal TotalPlatformDiscount { get; private set; }

    /// <summary>Frais de course, fixés par le devis relu au paiement.</summary>
    public decimal ShippingFee { get; private set; }

    /// <summary>LE DEVIS DE COURSE QUI A FIXÉ CES FRAIS.</summary>
    public string? DeliveryQuoteId { get; private set; }

    public decimal GrandTotal { get; private set; }

    /// <summary>Un mot du client pour la cuisine, valable pour toute la commande.</summary>
    public string? CustomerNote { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Pourquoi la commande a été mise en ARBITRAGE.</summary>
    public string? ReviewReason { get; private set; }

    /// <summary>Depuis quand elle attend une décision humaine.</summary>
    public DateTime? UnderReviewSinceUtc { get; private set; }

    // ── Adresse de livraison, FIGÉE ─────────────────────────────────────────

    public string? ShipToLabel { get; private set; }
    public string? ShipToRecipient { get; private set; }
    public string? ShipToPhone { get; private set; }
    public string? ShipToCommuneCode { get; private set; }
    public string? ShipToQuartier { get; private set; }
    public string? ShipToLandmark { get; private set; }
    public string? ShipToLine1 { get; private set; }
    public string? ShipToCountryCode { get; private set; }

    /// <summary>Position figée.</summary>
    public double? ShipToLatitude { get; private set; }

    public double? ShipToLongitude { get; private set; }

    /// <summary>Libellé de la commune, résolu à l'affichage.</summary>
    public string ShipToCommuneName => BeninGeography.CommuneName(ShipToCommuneCode);

    public IReadOnlyCollection<MealOrderLine> Lines => _lines.AsReadOnly();

    public static Result<MealOrder> Create(
        Guid buyerId,
        Guid restaurantId,
        Guid cartId,
        string currency,
        IEnumerable<MealOrderLineDraft> drafts,
        string? promotionCode = null,
        string? customerNote = null)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("food_ordering.buyer_required", "L'acheteur est obligatoire.");
        }

        if (restaurantId == Guid.Empty)
        {
            return Error.Validation("food_ordering.restaurant_required", "Le restaurant est obligatoire.");
        }

        var lignes = drafts?.ToList() ?? [];
        if (lignes.Count == 0)
        {
            return Error.Validation("food_ordering.no_lines", "Une commande doit comporter au moins une ligne.");
        }

        if (lignes.Any(d => d.Quantity <= 0))
        {
            return Error.Validation(
                "food_ordering.line_quantity_invalid", "Chaque ligne doit avoir une quantité positive.");
        }

        // SANS PLAT, LA COMMANDE EST INENVOYABLE EN CUISINE.
        if (lignes.Any(d => d.MenuItemId == Guid.Empty))
        {
            return Error.Validation(
                "food_ordering.line_incomplete", "Une ligne de repas doit désigner un plat.");
        }

        var commande = new MealOrder(
            MealOrderId.New(),
            buyerId,
            restaurantId,
            cartId,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim().ToUpperInvariant());

        commande.CustomerNote = Tronquer(customerNote, 500);

        foreach (var ligne in lignes)
        {
            commande._lines.Add(new MealOrderLine(Guid.NewGuid(), ligne));
        }

        commande.RecalculerTotaux();
        return commande;
    }

    /// <summary>Fige l'adresse de livraison choisie (copie intégrale dans la commande).</summary>
    public void SetShippingAddress(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1, string? countryCode,
        double? latitude, double? longitude)
    {
        ShipToLabel = Tronquer(label, 60);
        ShipToRecipient = Tronquer(recipient, 120);
        ShipToPhone = Tronquer(phone, 20);
        ShipToCommuneCode = Tronquer(communeCode, 40);
        ShipToQuartier = Tronquer(quartier, 120);
        ShipToLandmark = Tronquer(landmark, 200);
        ShipToLine1 = Tronquer(line1, 200);
        ShipToCountryCode = Tronquer(countryCode, 2) ?? BeninGeography.CountryCode;

        // Les deux ou aucune : une latitude seule placerait le point dans le golfe
        // de Guinée, à 400 km de Cotonou.
        ShipToLatitude = latitude is not null && longitude is not null ? latitude : null;
        ShipToLongitude = ShipToLatitude is null ? null : longitude;
    }

    /// <summary>Fixe les frais de course d'après le devis relu, et recalcule le total.</summary>
    public void SetShippingFee(decimal fee, string deliveryQuoteId)
    {
        ShippingFee = fee < 0m ? 0m : fee;
        DeliveryQuoteId = string.IsNullOrWhiteSpace(deliveryQuoteId) ? null : deliveryQuoteId;
        RecalculerTotaux();
    }

    private void RecalculerTotaux()
    {
        Subtotal = _lines.Sum(l => l.UnitBasePrice * l.Quantity);
        TotalSellerDiscount = _lines.Sum(l => l.SellerDiscount * l.Quantity);
        TotalPlatformDiscount = _lines.Sum(l => l.PlatformDiscount * l.Quantity);
        GrandTotal = _lines.Sum(l => l.LineTotal) + ShippingFee;
    }

    /// <summary>La commande est enregistrée et attend son paiement.</summary>
    public Result MarkAwaitingPayment()
    {
        if (Status != MealOrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "Transition invalide vers « en attente de paiement »."));
        }

        Status = MealOrderStatus.AwaitingPayment;
        Raise(new MealOrderPlacedDomainEvent(
            Id.Value, BuyerId, RestaurantId, CartId, GrandTotal, Currency));
        return Result.Success();
    }

    /// <summary>Paiement encaissé.</summary>
    public Result MarkPaid()
    {
        if (Status != MealOrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "Aucun paiement attendu dans cet état."));
        }

        Status = MealOrderStatus.Paid;
        return Result.Success();
    }

    /// <summary>La commande est confirmée : le ticket peut partir en cuisine.</summary>
    public Result Confirm()
    {
        if (Status != MealOrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "La commande doit être payée avant confirmation."));
        }

        Status = MealOrderStatus.Confirmed;

        // L'ÉVÉNEMENT PORTE LES LIGNES, ET C'EST TOUT L'INTÉRÊT.
        Raise(new MealOrderConfirmedDomainEvent(
            Id.Value, BuyerId, RestaurantId, GrandTotal, ShippingFee, Currency,
            PromotionCode, DeliveryQuoteId, CustomerNote,
            _lines
                .Select(l => new MealOrderConfirmedLine(
                    l.Id,
                    l.MenuItemId,
                    l.Name,
                    l.Quantity,
                    l.FinalUnitPrice,
                    l.Notes,
                    l.Options.Select(o => (o.OptionGroupId, o.OptionId)).ToList()))
                .ToList()));

        return Result.Success();
    }

    /// <summary>Le repas a été remis au client.</summary>
    public Result MarkDelivered()
    {
        // « EN ARBITRAGE » EST ACCEPTÉ ICI, ET C'EST DÉLIBÉRÉ.
        if (Status is not (MealOrderStatus.Confirmed or MealOrderStatus.UnderReview))
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "Seule une commande confirmée peut être marquée livrée."));
        }

        Status = MealOrderStatus.Delivered;
        Raise(new MealOrderDeliveredDomainEvent(Id.Value, BuyerId, RestaurantId));
        return Result.Success();
    }

    /// <summary>
    /// Annulation par le client ou par un échec de paiement, AVANT la confirmation.
    /// </summary>
    public Result Cancel(string reason)
    {
        // UNE COMMANDE CONFIRMÉE NE S'ANNULE PAS PAR ICI.
        if (Status is MealOrderStatus.Confirmed
            or MealOrderStatus.UnderReview
            or MealOrderStatus.Delivered
            or MealOrderStatus.Cancelled
            or MealOrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_cancellable", "La commande n'est plus annulable."));
        }

        Status = MealOrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new MealOrderCancelledDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>LE RESTAURANT A REFUSÉ — LA COMMANDE TOMBE APRÈS AVOIR ÉTÉ CONFIRMÉE.</summary>
    public Result RejectByRestaurant(string reason)
    {
        // « DÉJÀ LIVRÉE » SE DISTINGUE DE « DÉJÀ TERMINALE ».
        if (Status == MealOrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_delivered", "La commande a déjà été livrée."));
        }

        if (Status is MealOrderStatus.Cancelled or MealOrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_terminal", "La commande n'est plus refusable dans cet état."));
        }

        // AVANT LE PAIEMENT, IL N'Y A RIEN À REFUSER.
        if (Status is MealOrderStatus.Pending or MealOrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_yet_paid", "Une commande non payée ne se refuse pas, elle s'annule."));
        }

        Status = MealOrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new MealOrderCancelledDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>La commande est devenue inexécutable — elle passe en arbitrage.</summary>
    public Result MarkUnderReview(string reason)
    {
        if (Status == MealOrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_delivered", "La commande a déjà été livrée."));
        }

        // CODE PROPRE AU REJEU, PARCE QUE L'APPELANT DOIT POUVOIR L'AVALER.
        if (Status == MealOrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_under_review", "La commande est déjà en arbitrage."));
        }

        if (Status is MealOrderStatus.Cancelled or MealOrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_terminal", "La commande n'est plus arbitrable dans cet état."));
        }

        if (Status != MealOrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_confirmed", "Une commande non confirmée ne s'arbitre pas, elle s'annule."));
        }

        Status = MealOrderStatus.UnderReview;
        ReviewReason = reason;
        UnderReviewSinceUtc = DateTime.UtcNow;
        Raise(new MealOrderUnderReviewDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>L'exploitation a tranché : la commande REPART.</summary>
    public Result ResumeAfterReview()
    {
        if (Status != MealOrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = MealOrderStatus.Confirmed;

        // ON EFFACE LA DATE, PAS LE MOTIF.
        UnderReviewSinceUtc = null;

        Raise(new MealOrderResumedAfterReviewDomainEvent(
            Id.Value, BuyerId, RestaurantId, ReviewReason ?? string.Empty));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché dans l'autre sens : la vente est retournée, et le
    /// client sera remboursé.
    /// </summary>
    public Result CancelAfterReview(string reason)
    {
        if (Status != MealOrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = MealOrderStatus.Cancelled;
        CancellationReason = reason;
        UnderReviewSinceUtc = null;
        Raise(new MealOrderCancelledDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>Échec avant paiement (devis introuvable, restaurant fermé…).</summary>
    public void Fail(string reason)
    {
        Status = MealOrderStatus.Failed;
        CancellationReason = reason;
    }

    private static string? Tronquer(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var propre = value.Trim();
        return propre.Length > max ? propre[..max] : propre;
    }
}
