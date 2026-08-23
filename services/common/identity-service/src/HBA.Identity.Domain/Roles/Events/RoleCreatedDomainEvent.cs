using HBA.Shared.Domain.Events;

namespace HBA.Identity.Domain.Roles.Events;

public sealed record RoleCreatedDomainEvent(Guid RoleId, string Name) : DomainEvent;
