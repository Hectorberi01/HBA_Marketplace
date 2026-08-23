namespace HBA.Delivery.Route.Domain.Events;

public sealed record RouteCalculatedDomainEvent(Guid RoutePlanId, Guid? DeliveryId, int DistanceMeters, int DurationSeconds);

public sealed record RouteEtaRecalculatedDomainEvent(Guid DeliveryId, Guid RoutePlanId, DateTimeOffset EtaAt);
