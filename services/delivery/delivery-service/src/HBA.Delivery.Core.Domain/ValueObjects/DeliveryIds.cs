namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>Identité forte d'une livraison.</summary>
public readonly record struct DeliveryId(Guid Value)
{
    public static DeliveryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un livreur.</summary>
public readonly record struct DriverId(Guid Value)
{
    public static DriverId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
