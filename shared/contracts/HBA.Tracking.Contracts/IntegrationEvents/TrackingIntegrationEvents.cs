using HBA.Shared.IntegrationEvents;

namespace HBA.Tracking.Contracts.IntegrationEvents;

[HbaEvent("tracking.session-started", Version = 1, AggregateType = "TrackingSession")]
public sealed record TrackingSessionStartedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
}

[HbaEvent("tracking.location-sampled", Version = 1, AggregateType = "TrackingSession")]
public sealed record TrackingLocationSampledIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
    public required long Sequence { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
}

[HbaEvent("tracking.session-ended", Version = 1, AggregateType = "TrackingSession")]
public sealed record TrackingSessionEndedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
}

[HbaEvent("delivery.eta-updated", Version = 1, AggregateType = "Delivery")]
public sealed record DeliveryEtaUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required int EtaSeconds { get; init; }
}
