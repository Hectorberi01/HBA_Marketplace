namespace HBA.Delivery.Tracking.Domain.Events;

public sealed record TrackingSessionStartedDomainEvent(Guid DeliveryId, Guid DriverId);

public sealed record TrackingLocationAcceptedDomainEvent(Guid DeliveryId, Guid DriverId, long Sequence);

public sealed record TrackingSessionStoppedDomainEvent(Guid DeliveryId, Guid DriverId);
