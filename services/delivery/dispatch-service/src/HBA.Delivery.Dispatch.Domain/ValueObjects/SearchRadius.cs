namespace HBA.Delivery.Dispatch.Domain.ValueObjects;

public readonly record struct SearchRadius(int Meters)
{
    public static SearchRadius Create(int meters) => new(Math.Clamp(meters, 1500, 15000));
}
