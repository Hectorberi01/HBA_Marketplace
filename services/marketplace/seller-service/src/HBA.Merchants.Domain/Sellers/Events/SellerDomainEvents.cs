using HBA.Shared.Domain.Events;

namespace HBA.Merchants.Domain.Sellers.Events;

/// <summary>Un vendeur vient d'être onboardé (statut Pending).</summary>
public sealed record SellerRegisteredDomainEvent(Guid SellerId, Guid UserId, string ShopName) : DomainEvent;

/// <summary>Le vendeur déclare son dossier COMPLET et le soumet à la validation (§10.3).</summary>
public sealed record SellerKybSubmittedDomainEvent(
    Guid SellerId, Guid UserId, int DocumentCount) : DomainEvent;

/// <summary>Le KYB d'un vendeur a été validé.</summary>
public sealed record SellerKybVerifiedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le dossier KYB a été REFUSÉ par la modération.</summary>
public sealed record SellerKybRejectedDomainEvent(Guid SellerId, Guid UserId, string? Reason) : DomainEvent;

/// <summary>Un vendeur a été activé : il peut désormais publier des produits.</summary>
public sealed record SellerActivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>
/// Le vendeur a été SUSPENDU par l'exploitation : ses produits doivent quitter la
/// vente immédiatement.
/// </summary>
public sealed record SellerSuspendedDomainEvent(Guid SellerId, Guid UserId, string? Reason) : DomainEvent;

/// <summary>
/// La suspension a été levée : le catalogue retiré POUR CE MOTIF revient en vente.
/// </summary>
public sealed record SellerSuspensionLiftedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>
/// Le vendeur a fermé son compte (suppression partielle) : ses produits doivent
/// être retirés de la vente.
/// </summary>
public sealed record SellerClosedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le compte fermé du vendeur a été réactivé (validation admin).</summary>
public sealed record SellerReactivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le vendeur est supprimé définitivement (admin) : purge de ses produits.</summary>
public sealed record SellerDeletedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Une pièce KYB a été retirée du dossier.</summary>
public sealed record KybDocumentRemovedDomainEvent(Guid SellerId, Guid UserId, Guid MediaId) : DomainEvent;
