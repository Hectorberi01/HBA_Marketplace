namespace HBA.Delivery.Pricing.Domain.Entities;

public sealed record DeliveryZone(Guid Id, string Name, string GeometryRef, bool Active, bool Serviceable);
