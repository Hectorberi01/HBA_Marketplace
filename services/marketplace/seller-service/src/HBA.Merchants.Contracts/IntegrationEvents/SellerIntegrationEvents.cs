using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Contracts.IntegrationEvents;

/// <summary>Un vendeur a été onboardé.</summary>
[HbaEvent("merchant.seller.registered")]
public sealed record SellerRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public required string ShopName { get; init; }
}

/// <summary>Un vendeur est devenu actif : il peut publier des produits.</summary>
[HbaEvent("merchant.seller.activated")]
public sealed record SellerActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>Le vendeur a fermé son compte (suppression partielle).</summary>
[HbaEvent("merchant.seller.closed")]
public sealed record SellerClosedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Dossier KYB refusé. Consommé par Notifications, qui doit dire au vendeur CE
/// QU'IL DOIT CORRIGER — un refus sans motif n'est pas une décision de modération,
/// c'est une impasse.
/// </summary>
[HbaEvent("merchant.seller.kyb.rejected")]
public sealed record SellerKybRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Le vendeur a été suspendu par l'exploitation.</summary>
[HbaEvent("merchant.seller.suspended")]
public sealed record SellerSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// La suspension a été levée : le catalogue retiré pour ce motif revient en vente.
/// </summary>
[HbaEvent("merchant.seller.suspension.lifted")]
public sealed record SellerSuspensionLiftedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>Le compte fermé du vendeur a été réactivé (validation admin).</summary>
[HbaEvent("merchant.seller.reactivated")]
public sealed record SellerReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>Le vendeur est supprimé définitivement (admin).</summary>
[HbaEvent("merchant.seller.deleted")]
public sealed record SellerDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Une boutique ferme — décision du vendeur ou de la plateforme, indistinctement.
/// </summary>
[HbaEvent("merchant.store.closed")]
public sealed record StoreClosedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Une boutique rouvre : les offres retirées PAR CETTE FERMETURE reviennent en
/// vente.
/// </summary>
[HbaEvent("merchant.store.opened")]
public sealed record StoreOpenedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>UNE PIÈCE KYB A ÉTÉ RETIRÉE DU DOSSIER.</summary>
[HbaEvent("merchant.kyb.document.removed")]
public sealed record KybDocumentRemovedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid MediaId { get; init; }
}

/// <summary>Le dossier KYB est soumis à validation (§10.3 : `merchant.kyc.submitted`).</summary>
[HbaEvent("merchant.seller.kyb.submitted")]
public sealed record SellerKybSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>Ce que l'administrateur trouvera en ouvrant le dossier.</summary>
    public required int DocumentCount { get; init; }
}

/// <summary>Le dossier KYB est validé (§10.3 : `merchant.kyc.approved`).</summary>
[HbaEvent("merchant.seller.kyb.approved")]
public sealed record SellerKybApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>La plateforme a suspendu une boutique (§10.3 : `outlet.status.changed`).</summary>
[HbaEvent("merchant.store.suspended")]
public sealed record StoreSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// La sanction est levée. La boutique reste FERMÉE : c'est le vendeur qui rouvre.
/// </summary>
[HbaEvent("merchant.store.suspension.lifted")]
public sealed record StoreSuspensionLiftedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
}
