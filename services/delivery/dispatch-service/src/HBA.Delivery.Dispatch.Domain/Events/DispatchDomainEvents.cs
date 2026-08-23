using HBA.Shared.Domain.Events;

namespace HBA.Delivery.Dispatch.Domain.Events;

public sealed record DispatchRequestedDomainEvent(Guid DispatchJobId, Guid DeliveryId) : DomainEvent;

public sealed record DeliveryOfferCreatedDomainEvent(Guid DeliveryId, Guid DriverId, Guid OfferId) : DomainEvent;

public sealed record DeliveryOfferExpiredDomainEvent(Guid DeliveryId, Guid DriverId, Guid OfferId) : DomainEvent;

public sealed record DriverAssignedDomainEvent(Guid DeliveryId, Guid DriverId, Guid AssignmentId) : DomainEvent;

public sealed record DispatchCancelledDomainEvent(Guid DispatchJobId, Guid DeliveryId, string? Reason) : DomainEvent;
