namespace HBA.Orders.Domain.Orders;

/// <summary>Identité forte d'une commande.</summary>
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// États de la commande, pilotés par le Saga d'orchestration : création →
/// réservation du stock → paiement → confirmation, avec compensations.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    AwaitingPayment = 1,
    Paid = 2,
    Confirmed = 3,
    Cancelled = 4,
    Failed = 5,
    Delivered = 6,

    /// <summary>LA COMMANDE EST PAYÉE MAIS PLUS EXÉCUTABLE : ELLE ATTEND UN ARBITRAGE.</summary>
    UnderReview = 7
}
