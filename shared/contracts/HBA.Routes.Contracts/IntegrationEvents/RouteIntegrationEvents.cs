using HBA.Shared.IntegrationEvents;

namespace HBA.Routes.Contracts.IntegrationEvents;

[HbaEvent("route.calculated", Version = 1, AggregateType = "RoutePlan")]
public sealed record RouteCalculatedIntegrationEvent : IntegrationEvent
{
    public required Guid RoutePlanId { get; init; }
    public Guid? DeliveryId { get; init; }
    public required int DistanceMeters { get; init; }
    public required int DurationSeconds { get; init; }
}

[HbaEvent("route.recalculated", Version = 1, AggregateType = "EtaSnapshot")]
public sealed record RouteRecalculatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid RoutePlanId { get; init; }
    public required DateTime EtaAtUtc { get; init; }
}

[HbaEvent("delivery.eta-updated", Version = 1, AggregateType = "Delivery")]
public sealed record RouteDeliveryEtaUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required DateTime EtaAtUtc { get; init; }
}
