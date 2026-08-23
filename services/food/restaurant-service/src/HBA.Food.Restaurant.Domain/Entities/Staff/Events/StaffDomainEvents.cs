using HBA.Shared.Domain.Events;

namespace HBA.Food.Domain.Staff.Events;

/// <summary>
/// ─────────────────────────────────────────────────────────────────────────────
/// LES MOUVEMENTS DE PERSONNEL SONT DES ÉVÉNEMENTS, PAS DES ÉCRITURES.
///
/// Le cahier des charges (§21) demande de journaliser « ajout/suppression
/// employé, changement de rôle ». Un journal reconstruit après coup depuis les
/// lignes en base ne dirait que l'état final : il ne dirait pas qu'un cuisinier a
/// été manager pendant six heures un samedi soir.
///
/// Les rôles voyagent en CHAÎNE et non en énumération : ces événements sortent du
/// module, et un consommateur qui devrait référencer <c>StaffRole</c> ferait
/// exactement la dépendance que la frontière de Food interdit.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed record StaffHiredDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string Role) : DomainEvent;

public sealed record StaffRoleChangedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string PreviousRole, string NewRole) : DomainEvent;

/// <summary>
/// Une dérogation nominative. <c>IsGranted</c> distingue l'octroi du retrait —
/// et c'est le retrait qui compte le plus dans un audit : il explique pourquoi
/// quelqu'un ne pouvait plus faire ce que son rôle prévoyait.
/// </summary>
public sealed record StaffPermissionChangedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string Permission, bool IsGranted) : DomainEvent;

public sealed record StaffDeactivatedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId) : DomainEvent;

public sealed record StaffReactivatedDomainEvent(
    Guid StaffId, Guid RestaurantId, Guid UserId, string Role) : DomainEvent;
