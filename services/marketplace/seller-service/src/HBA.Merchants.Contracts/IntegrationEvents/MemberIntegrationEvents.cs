using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Contracts.IntegrationEvents;

/// <summary>UNE INVITATION EST PARTIE — ET CET ÉVÉNEMENT PORTE LE JETON EN CLAIR.</summary>
[HbaEvent("merchant.seller.member.invited")]
public sealed record SellerMemberInvitedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid InvitationId { get; init; }

    public required string Email { get; init; }

    /// <summary>Le nom saisi par celui qui invite, pour personnaliser le message.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Le nom de la boutique — l'invité doit savoir QUI l'invite.</summary>
    public required string ShopName { get; init; }

    /// <summary>SECRET, ET DÉSORMAIS CHIFFRÉ (AES-GCM, `ISecretProtector`).</summary>
    public required string ProtectedInvitationToken { get; init; }

    public required DateTime ExpiresOnUtc { get; init; }
}

/// <summary>UN MEMBRE A REJOINT L'ÉQUIPE — L'ÉVÉNEMENT QUI REND LE MEMBRE UTILISABLE.</summary>
[HbaEvent("merchant.seller.member.joined")]
public sealed record SellerMemberJoinedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>Les rôles au niveau du vendeur.</summary>
    public required IReadOnlyList<Guid> SellerRoleIds { get; init; }

    public required IReadOnlyList<Guid> StoreIds { get; init; }
}

/// <summary>Les rôles d'un membre ont changé.</summary>
[HbaEvent("merchant.seller.member.roles.updated")]
public sealed record SellerMemberRolesUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    public required IReadOnlyList<Guid> SellerRoleIds { get; init; }
}

/// <summary>Un membre a été affecté à une boutique.</summary>
[HbaEvent("merchant.seller.member.store.assigned")]
public sealed record SellerMemberStoreAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    public required Guid StoreId { get; init; }
}

/// <summary>Un membre a été retiré d'une boutique.</summary>
[HbaEvent("merchant.seller.member.store.unassigned")]
public sealed record SellerMemberStoreUnassignedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    public required Guid StoreId { get; init; }
}

/// <summary>L'accès d'un membre est suspendu.</summary>
[HbaEvent("merchant.seller.member.suspended")]
public sealed record SellerMemberSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }
}

/// <summary>L'accès d'un membre suspendu est rouvert.</summary>
[HbaEvent("merchant.seller.member.activated")]
public sealed record SellerMemberActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }
}

/// <summary>Un membre est sorti de l'équipe — révoqué ou parti de lui-même.</summary>
[HbaEvent("merchant.seller.member.revoked")]
public sealed record SellerMemberRevokedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid MemberId { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>Le compte appartient-il encore à une AUTRE équipe vendeur ?</summary>
    public required bool HasOtherSellerMembership { get; init; }
}

/// <summary>La propriété du dossier a changé de porteur.</summary>
[HbaEvent("merchant.seller.ownership.transferred")]
public sealed record SellerOwnershipTransferredIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    public required Guid PreviousOwnerMemberId { get; init; }

    public required Guid PreviousOwnerUserId { get; init; }

    public required Guid NewOwnerMemberId { get; init; }

    public required Guid NewOwnerUserId { get; init; }
}
