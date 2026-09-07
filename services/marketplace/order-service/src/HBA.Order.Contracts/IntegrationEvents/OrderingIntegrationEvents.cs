using HBA.Shared.IntegrationEvents;

namespace HBA.Orders.Contracts.IntegrationEvents;

/// <summary>Commande placée (stock réservé, en attente de paiement).</summary>
[HbaEvent("order.placed")]
public sealed record OrderPlacedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid CartId { get; init; }
    public required decimal GrandTotal { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Part d'UN vendeur dans une commande : ce qu'il a vendu, et pour combien.</summary>
/// <param name="SellerId">Le vendeur concerné.</param>
/// <param name="ItemCount">Nombre d'articles de CE vendeur.</param>
/// <param name="Amount">
/// Montant acheté chez CE vendeur (prix final, remises comprises, AVANT commission
/// de la plateforme).
/// </param>
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>Commande confirmée (paiement encaissé).</summary>
[HbaEvent("order.confirmed")]
public sealed record OrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Currency { get; init; }

    /// <summary>Code promo utilisé par cette commande, ou null.</summary>
    public string? PromotionCode { get; init; }

    /// <summary>Les vendeurs concernés, et la part de chacun.</summary>
    public required IReadOnlyCollection<OrderSellerShare> SellerShares { get; init; }

    /// <summary>La nature de la commande : « Goods » ou « Food ».</summary>
    public string Kind { get; init; } = "Goods";

    /// <summary>L'établissement qui doit préparer la commande.</summary>
    public Guid? RestaurantId { get; init; }
}

/// <summary>Commande annulée. Consommé par Notifications / analytics.</summary>
[HbaEvent("order.cancelled")]
public sealed record OrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Reason { get; init; }

    /// <summary>
    /// Les vendeurs concernés et la part perdue par chacun, ou <c> null</c> pour un
    /// message émis avant l'ajout du champ.
    /// </summary>
    public IReadOnlyCollection<OrderSellerShare>? SellerShares { get; init; }

    /// <summary>La devise des montants ci-dessus.</summary>
    public string? Currency { get; init; }
}

/// <summary>
/// Commande livrée. Consommé par Payments (libération escrow) et Settlement
/// (payout).
/// </summary>
[HbaEvent("order.delivered")]
public sealed record OrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}

/// <summary>COMMANDE PAYÉE MAIS DEVENUE INEXÉCUTABLE : ELLE ATTEND UN ARBITRAGE HUMAIN.</summary>
[HbaEvent("order.under.review")]
public sealed record OrderUnderReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }

    /// <summary>
    /// En clair, et destiné à être lu par un humain : « la course a été annulée
    /// (livreur indisponible) », « commande expédiée depuis 2 lieux ».
    /// </summary>
    public required string Reason { get; init; }
}

/// <summary>
/// L'arbitrage a conclu à la REPRISE : la commande repart, une course va être
/// redemandée.
/// </summary>
[HbaEvent("order.resumed.after.review")]
public sealed record OrderResumedAfterReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}

/// <summary>Une ligne qu'un vendeur n'honorera pas.</summary>
/// <param name="ShipFromLocationId">
/// INDISPENSABLE, ET FACILE À OUBLIER. Inventory travaille par (SKU, emplacement,
/// commande) : sans l'emplacement, un consommateur ne peut PAS rendre le stock de
/// cette ligne, et il ne le découvrirait qu'en écrivant son gestionnaire.
/// </param>
public sealed record SellerOrderRefusedLine(
    Guid OrderLineId,
    Guid ProductId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal LineTotal);

/// <summary>UN VENDEUR N'HONORERA PAS SA PART D'UNE COMMANDE DÉJÀ PAYÉE (ISSUE-027).</summary>
[HbaEvent("order.seller.order.refused")]
public sealed record SellerOrderRefusedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required string Currency { get; init; }

    /// <summary>« Rejected » (refusée avant engagement) ou « Cancelled » (dédite après).</summary>
    public required string Outcome { get; init; }

    /// <summary>En clair, écrit par le vendeur.</summary>
    public required string Reason { get; init; }

    /// <summary>Montant PAYÉ pour la part, remises comprises.</summary>
    public required decimal Amount { get; init; }

    public required IReadOnlyCollection<SellerOrderRefusedLine> Lines { get; init; }
}
