using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Orders.Domain.Orders.Events;

namespace HBA.Orders.Domain.Orders;

/// <summary>
/// Commande multi-vendeur. Fige le prix de chaque ligne (prix de base + réductions
/// par financeur) au moment de l'achat.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();
    private readonly List<OrderReturnSettlement> _returnSettlements = new();

    private Order()
    {
    }

    private Order(OrderId id, Guid buyerId, Guid cartId, string currency, string? promotionCode)
        : base(id)
    {
        BuyerId = buyerId;
        CartId = cartId;
        Currency = currency;
        PromotionCode = promotionCode;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BuyerId { get; private set; }
    public Guid CartId { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>Code promo appliqué au panier, FIGÉ au moment de la commande.</summary>
    public string? PromotionCode { get; private set; }

    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal TotalSellerDiscount { get; private set; }
    public decimal TotalPlatformDiscount { get; private set; }

    /// <summary>
    /// Frais de livraison encaissés par la plateforme (forfait choisi au checkout).
    /// </summary>
    public decimal ShippingFee { get; private set; }

    /// <summary>LE DEVIS DE COURSE QUI A FIXÉ CES FRAIS. Restauration seulement.</summary>
    public string? DeliveryQuoteId { get; private set; }

    public decimal GrandTotal { get; private set; }

    /// <summary>Identifiant du paiement capture, fige pour les retours/remboursements.</summary>
    public Guid? PaymentId { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Pourquoi la commande a été mise en ARBITRAGE. Nul tant qu'elle ne l'a jamais
    /// été.
    /// </summary>
    public string? ReviewReason { get; private set; }

    /// <summary>Depuis quand elle attend une décision humaine.</summary>
    public DateTime? UnderReviewSinceUtc { get; private set; }

    // Adresse de livraison FIGÉE au moment de la commande.
    public string? ShipToLabel { get; private set; }
    public string? ShipToRecipient { get; private set; }
    public string? ShipToPhone { get; private set; }
    public string? ShipToCommuneCode { get; private set; }
    public string? ShipToQuartier { get; private set; }
    public string? ShipToLandmark { get; private set; }
    public string? ShipToLine1 { get; private set; }
    public string? ShipToCountryCode { get; private set; }

    /// <summary>Position figée, quand l'acheteur en avait une.</summary>
    public double? ShipToLatitude { get; private set; }

    public double? ShipToLongitude { get; private set; }

    /// <summary>La commande porte-t-elle un point ouvrable dans une carto ?</summary>
    public bool HasShipToCoordinates => ShipToLatitude is not null && ShipToLongitude is not null;

    /// <summary>Libellé de la commune de livraison, résolu à l'affichage.</summary>
    public string ShipToCommuneName => BeninGeography.CommuneName(ShipToCommuneCode);

    /// <summary>La commande porte-t-elle une adresse exploitable ?</summary>
    public bool HasShippingAddress =>
        !string.IsNullOrWhiteSpace(ShipToCommuneCode) && !string.IsNullOrWhiteSpace(ShipToLandmark);

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    /// <summary>Ce que les dossiers de retour ont définitivement retiré à cette commande.</summary>
    public IReadOnlyCollection<OrderReturnSettlement> ReturnSettlements => _returnSettlements.AsReadOnly();

    /// <summary>
    /// Ce qui a DÉJÀ été rendu au client sur cette commande, tous dossiers de
    /// retour confondus.
    /// </summary>
    public decimal RefundedAmount => _returnSettlements.Sum(s => s.RefundedAmount);

    /// <summary>
    /// Combien d'exemplaires de cette ligne sont DÉJÀ revenus, tous dossiers
    /// confondus.
    /// </summary>
    public int ReturnedQuantityFor(Guid orderItemId)
    {
        var reprise = _returnSettlements.Sum(s => s.QuantityFor(orderItemId));
        var ligne = _lines.FirstOrDefault(l => l.Id == orderItemId);
        return ligne is null ? reprise : Math.Min(reprise, ligne.Quantity);
    }

    /// <summary>Enregistre ce qu'un dossier de retour a rendu et repris.</summary>
    public Result RecordReturnSettlement(
        Guid returnRequestId,
        decimal totalRefunded,
        IReadOnlyCollection<ReturnSettlementLineDraft> lines,
        DateTime nowUtc)
    {
        if (returnRequestId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "order.return_settlement.identity_required",
                "Le dossier de retour est obligatoire."));
        }

        if (totalRefunded < 0m)
        {
            return Result.Failure(Error.Validation(
                "order.return_settlement.amount_invalid",
                "Un montant rembourse ne peut pas etre negatif."));
        }

        var connues = lines
            .Where(l => _lines.Any(ligne => ligne.Id == l.OrderItemId))
            .ToList();

        var dossier = _returnSettlements.FirstOrDefault(s => s.ReturnRequestId == returnRequestId);
        if (dossier is null)
        {
            dossier = new OrderReturnSettlement(Guid.NewGuid(), returnRequestId, nowUtc);
            _returnSettlements.Add(dossier);
        }

        dossier.Retenir(totalRefunded, connues, nowUtc);
        return Result.Success();
    }

    /// <summary>La nature de la commande.</summary>
    public OrderLineKind Kind => _lines.Count == 0 ? OrderLineKind.Goods : _lines[0].Kind;

    /// <summary>
    /// L'établissement qui prépare cette commande, ou <c> null</c> si ce n'est pas
    /// une commande de repas.
    /// </summary>
    public Guid? RestaurantId => Kind == OrderLineKind.Food && _lines.Count > 0
        ? _lines[0].RestaurantId
        : null;

    public static Result<Order> Create(
        Guid buyerId,
        Guid cartId,
        string currency,
        IEnumerable<OrderLineDraft> drafts,
        string? promotionCode = null)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("ordering.buyer_required", "L'acheteur est obligatoire.");
        }

        var draftList = drafts?.ToList() ?? new List<OrderLineDraft>();
        if (draftList.Count == 0)
        {
            return Error.Validation("ordering.no_lines", "Une commande doit comporter au moins une ligne.");
        }

        if (draftList.Any(d => d.Quantity <= 0))
        {
            return Error.Validation("ordering.line_quantity_invalid", "Chaque ligne doit avoir une quantité positive.");
        }

        // UNE COMMANDE NE MÉLANGE PAS LES DEUX NATURES.
        if (draftList.Select(d => d.Kind).Distinct().Count() > 1)
        {
            return Error.Validation(
                "ordering.mixed_kinds", "Une commande ne peut pas mêler des plats et des articles.");
        }

        var foodDrafts = draftList.Where(d => d.Kind == OrderLineKind.Food).ToList();

        if (foodDrafts.Count > 0)
        {
            // SANS RESTAURANT NI PLAT, LA COMMANDE EST INENVOYABLE EN CUISINE.
            if (foodDrafts.Any(d => d.RestaurantId == Guid.Empty || d.MenuItemId == Guid.Empty))
            {
                return Error.Validation(
                    "ordering.food_line_incomplete", "Une ligne de repas doit désigner un restaurant et un plat.");
            }

            // Deux cuisines, ce sont deux temps de préparation et deux collectes :
            // le livreur attendrait la plus lente en laissant refroidir l'autre.
            if (foodDrafts.Select(d => d.RestaurantId).Distinct().Count() > 1)
            {
                return Error.Validation(
                    "ordering.multiple_restaurants", "Une commande ne peut concerner qu'un seul restaurant.");
            }
        }

        var order = new Order(
            OrderId.New(),
            buyerId,
            cartId,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim().ToUpperInvariant());

        foreach (var draft in draftList)
        {
            order._lines.Add(new OrderLine(Guid.NewGuid(), draft));
        }

        order.RecomputeTotals();
        return order;
    }

    /// <summary>Fige l'adresse de livraison choisie (copie intégrale dans la commande).</summary>
    public void SetShippingAddress(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1, string? countryCode,
        double? latitude, double? longitude)
    {
        ShipToLabel = Trim(label, 60);
        ShipToRecipient = Trim(recipient, 120);
        ShipToPhone = Trim(phone, 20);
        ShipToCommuneCode = Trim(communeCode, 40);
        ShipToQuartier = Trim(quartier, 120);
        ShipToLandmark = Trim(landmark, 200);
        ShipToLine1 = Trim(line1, 200);
        ShipToCountryCode = Trim(countryCode, 2) ?? BeninGeography.CountryCode;

        // Les deux ou aucune : une latitude seule placerait le point dans le golfe
        // de Guinée, à 400 km de Cotonou.
        ShipToLatitude = latitude is not null && longitude is not null ? latitude : null;
        ShipToLongitude = ShipToLatitude is null ? null : longitude;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>Fixe les frais de livraison (forfait choisi) et recalcule le total.</summary>
    public void SetShippingFee(decimal fee, string? deliveryQuoteId = null)
    {
        ShippingFee = fee < 0m ? 0m : fee;
        DeliveryQuoteId = string.IsNullOrWhiteSpace(deliveryQuoteId) ? null : deliveryQuoteId;
        RecomputeTotals();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.UnitBasePrice * l.Quantity);
        TotalSellerDiscount = _lines.Sum(l => l.SellerDiscount * l.Quantity);
        TotalPlatformDiscount = _lines.Sum(l => l.PlatformDiscount * l.Quantity);
        GrandTotal = _lines.Sum(l => l.LineTotal) + ShippingFee;
    }

    /// <summary>Stock réservé : la commande attend le paiement.</summary>
    public Result MarkAwaitingPayment()
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Transition invalide vers « en attente de paiement »."));
        }

        Status = OrderStatus.AwaitingPayment;
        Raise(new OrderPlacedDomainEvent(Id.Value, BuyerId, CartId, GrandTotal, Currency));
        return Result.Success();
    }

    /// <summary>Paiement encaissé.</summary>
    public Result MarkPaid(Guid paymentId)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Aucun paiement attendu dans cet état."));
        }

        if (paymentId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("ordering.payment_required", "Le paiement capture est obligatoire."));
        }

        PaymentId = paymentId;
        Status = OrderStatus.Paid;
        return Result.Success();
    }

    /// <summary>Réservations soldées : la commande est confirmée.</summary>
    public Result Confirm()
    {
        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "La commande doit être payée avant confirmation."));
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(
            Id.Value, BuyerId, Currency, PromotionCode, BuildSellerShares(),
            Kind.ToString(), RestaurantId));
        return Result.Success();
    }

    /// <summary>
    /// Répartit la commande entre ses vendeurs : combien d'articles, pour quel
    /// montant.
    /// </summary>
    private List<OrderSellerShare> BuildSellerShares()
        => SellerLineGroups()
            .Select(g => new OrderSellerShare(
                g.Key,
                g.Sum(line => line.Quantity),
                g.Sum(line => line.LineTotal)))
            .ToList();

    /// <summary>Les lignes qui appartiennent à un VENDEUR, groupées par vendeur.</summary>
    internal IEnumerable<IGrouping<Guid, OrderLine>> SellerLineGroups()
        => _lines
            .Where(line => line.Kind == OrderLineKind.Goods)
            .GroupBy(line => line.SellerId);

    /// <summary>Livraison confirmée (toutes les expéditions reçues).</summary>
    public Result MarkDelivered()
    {
        // « EN ARBITRAGE » EST ACCEPTÉ ICI, ET C'EST DÉLIBÉRÉ.
        if (Status is not (OrderStatus.Confirmed or OrderStatus.UnderReview))
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Seule une commande confirmée peut être marquée livrée."));
        }

        Status = OrderStatus.Delivered;
        Raise(new OrderDeliveredDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }

    /// <summary>Annulation (réservations à libérer en compensation).</summary>
    public Result Cancel(string reason)
    {
        // « EN ARBITRAGE » EST REFUSÉ ICI AU MÊME TITRE QUE « CONFIRMÉE ».
        if (Status is OrderStatus.Confirmed
            or OrderStatus.UnderReview
            or OrderStatus.Cancelled
            or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict("ordering.not_cancellable", "La commande n'est plus annulable."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new OrderCancelledDomainEvent(
            Id.Value, BuyerId, reason, BuildSellerShares(), Currency));
        return Result.Success();
    }

    /// <summary>LE RESTAURANT A REFUSÉ — LA COMMANDE TOMBE APRÈS AVOIR ÉTÉ CONFIRMÉE.</summary>
    public Result RejectByProvider(string reason)
    {
        if (Kind != OrderLineKind.Food)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_food", "Seule une commande de repas peut être refusée par son prestataire."));
        }

        // DEUX CODES DISTINCTS, ET LA DISTINCTION EST FONCTIONNELLE.
        if (Status == OrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_delivered", "La commande a déjà été livrée."));
        }

        if (Status is OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_terminal", "La commande n'est plus refusable dans cet état."));
        }

        // AVANT LE PAIEMENT, IL N'Y A RIEN À REFUSER.
        if (Status is OrderStatus.Pending or OrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_yet_paid", "Une commande non payée ne se refuse pas, elle s'annule."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new OrderCancelledDomainEvent(
            Id.Value, BuyerId, reason, BuildSellerShares(), Currency));
        return Result.Success();
    }

    /// <summary>LA COMMANDE EST DEVENUE INEXÉCUTABLE — ELLE PASSE EN ARBITRAGE.</summary>
    public Result MarkUnderReview(string reason)
    {
        // « DÉJÀ LIVRÉE » SE DISTINGUE DE « DÉJÀ TERMINALE », comme dans
        // `RejectByProvider`, et pour la même raison.
        if (Status == OrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_delivered", "La commande a déjà été livrée."));
        }

        // CODE PROPRE AU REJEU, PARCE QUE L'APPELANT DOIT POUVOIR L'AVALER.
        if (Status == OrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_under_review", "La commande est déjà en arbitrage."));
        }

        if (Status is OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_terminal", "La commande n'est plus arbitrable dans cet état."));
        }

        // AVANT LA CONFIRMATION, IL N'Y A RIEN À ARBITRER.
        if (Status != OrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_confirmed", "Une commande non confirmée ne s'arbitre pas, elle s'annule."));
        }

        Status = OrderStatus.UnderReview;
        ReviewReason = reason;
        UnderReviewSinceUtc = DateTime.UtcNow;
        Raise(new OrderUnderReviewDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché : la commande REPART. Elle redevient une commande
    /// confirmée ordinaire.
    /// </summary>
    public Result ResumeAfterReview()
    {
        if (Status != OrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = OrderStatus.Confirmed;

        // ON EFFACE LA DATE, PAS LE MOTIF.
        UnderReviewSinceUtc = null;

        Raise(new OrderResumedAfterReviewDomainEvent(Id.Value, BuyerId, ReviewReason ?? string.Empty));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché dans l'autre sens : la vente est retournée, et
    /// l'acheteur sera remboursé.
    /// </summary>
    public Result CancelAfterReview(string reason)
    {
        if (Status != OrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UnderReviewSinceUtc = null;
        Raise(new OrderCancelledDomainEvent(
            Id.Value, BuyerId, reason, BuildSellerShares(), Currency));
        return Result.Success();
    }

    /// <summary>Échec du Saga (ex. stock indisponible) avant paiement.</summary>
    public void Fail(string reason)
    {
        Status = OrderStatus.Failed;
        CancellationReason = reason;
    }
}
