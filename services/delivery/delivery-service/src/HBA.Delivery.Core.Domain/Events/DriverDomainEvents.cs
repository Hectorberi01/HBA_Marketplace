// DÉPLACÉ DEPUIS `driver-service/src/HBA.Delivery.Driver.Domain/Events` (lot 5.4,
// ISSUE-069).

using HBA.Shared.Domain.Events;

namespace HBA.Deliveries.Domain.Drivers.Events;

/// <summary>L'EXPLOITATION A VÉRIFIÉ LES PIÈCES D'UN LIVREUR.</summary>
public sealed record DriverVerifiedDomainEvent(Guid DriverId, Guid UserId) : DomainEvent;
