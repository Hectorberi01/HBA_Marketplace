namespace HBA.Delivery.Dispatch.Domain.Entities;

public sealed record DeliveryOffer(
    Guid Id,
    Guid DeliveryId,
    Guid DriverId,
    DateTimeOffset OfferedAt,
    DateTimeOffset ExpiresAt,
    string Status);
