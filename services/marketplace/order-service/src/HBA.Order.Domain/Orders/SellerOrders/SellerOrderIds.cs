namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>Identité forte d'une commande vendeur.</summary>
public readonly record struct SellerOrderId(Guid Value)
{
    public static SellerOrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>CE QUE LE VENDEUR A À FAIRE DE SA PART DE COMMANDE.</summary>
public enum SellerOrderStatus
{
    AwaitingConfirmation = 0,
    Confirmed = 1,
    Preparing = 2,
    ReadyForPickup = 3,
    HandedOver = 4,
    Rejected = 5,
    Cancelled = 6
}
