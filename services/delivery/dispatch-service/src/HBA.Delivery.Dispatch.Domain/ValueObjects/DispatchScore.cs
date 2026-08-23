namespace HBA.Delivery.Dispatch.Domain.ValueObjects;

public readonly record struct DispatchScore(decimal Value)
{
    public static DispatchScore From(decimal value) => new(Math.Clamp(value, 0m, 1m));
}
