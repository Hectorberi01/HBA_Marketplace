namespace HBA.FoodOrders.Domain.Orders;

/// <summary>Identité forte d'une commande de repas.</summary>
public readonly record struct MealOrderId(Guid Value)
{
    public static MealOrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>États de la commande de repas.</summary>
public enum MealOrderStatus
{
    Pending = 0,
    AwaitingPayment = 1,
    Paid = 2,
    Confirmed = 3,
    Cancelled = 4,
    Failed = 5,
    Delivered = 6,

    /// <summary>Payée mais plus exécutable : elle attend un arbitrage humain.</summary>
    UnderReview = 7
}
