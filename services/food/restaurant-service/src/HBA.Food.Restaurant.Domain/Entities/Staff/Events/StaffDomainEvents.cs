using HBA.Shared.Domain.Events;

namespace HBA.Food.Domain.Staff.Events;

/// <summary>LES MOUVEMENTS DE PERSONNEL SONT DES ÉVÉNEMENTS, PAS DES ÉCRITURES.</summary>
public sealed record StaffHiredDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string Role) : DomainEvent;

public sealed record StaffRoleChangedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string PreviousRole, string NewRole) : DomainEvent;

/// <summary>Une dérogation nominative.</summary>
public sealed record StaffPermissionChangedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string Permission, bool IsGranted) : DomainEvent;

public sealed record StaffDeactivatedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId) : DomainEvent;

public sealed record StaffReactivatedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string Role) : DomainEvent;
