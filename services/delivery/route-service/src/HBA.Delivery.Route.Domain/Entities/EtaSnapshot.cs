using HBA.Delivery.Route.Domain.ValueObjects;

namespace HBA.Delivery.Route.Domain.Entities;

public sealed record EtaSnapshot(
    Guid Id,
    Guid DeliveryId,
    Guid RoutePlanId,
    DateTimeOffset EtaAt,
    int RemainingMeters,
    DateTimeOffset ComputedAt,
    string Source,
    RouteEstimate? Estimate);
