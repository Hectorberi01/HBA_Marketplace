using HBA.Shared.Domain.Events;

namespace HBA.Merchants.Domain.Members.Events;

/// <summary>LES ÉVÉNEMENTS DE L'APPARTENANCE — ET LE PLUS IMPORTANT DE TOUS.</summary>
public sealed record SellerMemberJoinedDomainEvent(
    Guid MemberId,
    Guid SellerId,
    Guid UserId,
    IReadOnlyList<Guid> SellerRoleIds,
    IReadOnlyList<Guid> StoreIds) : DomainEvent;

/// <summary>Les rôles d'un membre ont changé.</summary>
public sealed record SellerMemberRolesChangedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, IReadOnlyList<Guid> SellerRoleIds) : DomainEvent;

public sealed record SellerMemberStoreAssignedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, Guid StoreId) : DomainEvent;

public sealed record SellerMemberStoreUnassignedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, Guid StoreId) : DomainEvent;

/// <summary>L'accès est suspendu.</summary>
public sealed record SellerMemberSuspendedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId) : DomainEvent;

public sealed record SellerMemberActivatedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>L'accès est révoqué, ou le membre est parti de lui-même.</summary>
/// <param name="HasOtherSellerMembership">
/// Le compte appartient-il encore à une AUTRE équipe vendeur ?
/// </param>
public sealed record SellerMemberRevokedDomainEvent(
    Guid MemberId, Guid SellerId, Guid UserId, bool HasOtherSellerMembership) : DomainEvent;

// IL N'Y A PAS D'ÉVÉNEMENT DE DOMAINE POUR L'INVITATION, ET C'EST DÉLIBÉRÉ.

/// <summary>LA PROPRIÉTÉ DU DOSSIER A CHANGÉ DE PORTEUR.</summary>
public sealed record SellerOwnershipTransferredDomainEvent(
    Guid SellerId,
    Guid PreviousOwnerMemberId,
    Guid PreviousOwnerUserId,
    Guid NewOwnerMemberId,
    Guid NewOwnerUserId) : DomainEvent;
