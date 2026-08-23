using HBA.Shared.Domain.Events;

namespace HBA.Merchants.Domain.Sellers.Events;

/// <summary>Un vendeur vient d'être onboardé (statut Pending).</summary>
public sealed record SellerRegisteredDomainEvent(Guid SellerId, Guid UserId, string ShopName) : DomainEvent;

/// <summary>
/// Le vendeur déclare son dossier COMPLET et le soumet à la validation (§10.3).
///
/// CE N'EST PAS « UNE PIÈCE A ÉTÉ DÉPOSÉE ». C'est le vendeur qui dit avoir
/// fini. La distinction est ce qui sépare une file d'attente d'administrateurs
/// remplie de dossiers exploitables d'une file remplie de dossiers en cours.
///
/// Le nombre de pièces voyage avec l'événement : c'est ce que la notification
/// affiche à l'administrateur pour qu'il sache ce qui l'attend avant d'ouvrir.
/// </summary>
public sealed record SellerKybSubmittedDomainEvent(
    Guid SellerId, Guid UserId, int DocumentCount) : DomainEvent;

/// <summary>Le KYB d'un vendeur a été validé.</summary>
public sealed record SellerKybVerifiedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>
/// Le dossier KYB a été REFUSÉ par la modération.
///
/// Le motif voyage avec l'événement : c'est lui que le vendeur recevra. Sans
/// motif, il redéposera la même pièce et le refus se répétera.
/// </summary>
public sealed record SellerKybRejectedDomainEvent(Guid SellerId, Guid UserId, string? Reason) : DomainEvent;

/// <summary>Un vendeur a été activé : il peut désormais publier des produits.</summary>
public sealed record SellerActivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>
/// Le vendeur a été SUSPENDU par l'exploitation : ses produits doivent quitter la
/// vente immédiatement.
///
/// Cet événement n'existait pas, et c'est pourquoi la suspension n'avait aucun
/// effet visible pour les acheteurs. Le motif est transporté pour que la trace
/// reste lisible en aval — le catalogue saura pourquoi il a été retiré.
/// </summary>
public sealed record SellerSuspendedDomainEvent(Guid SellerId, Guid UserId, string? Reason) : DomainEvent;

/// <summary>La suspension a été levée : le catalogue retiré POUR CE MOTIF revient en vente.</summary>
public sealed record SellerSuspensionLiftedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le vendeur a fermé son compte (suppression partielle) : ses produits doivent être retirés de la vente.</summary>
public sealed record SellerClosedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le compte fermé du vendeur a été réactivé (validation admin).</summary>
public sealed record SellerReactivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le vendeur est supprimé définitivement (admin) : purge de ses produits.</summary>
public sealed record SellerDeletedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>
/// Une pièce KYB a été retirée du dossier.
///
/// SON SEUL RÔLE EST DE FAIRE EFFACER LE FICHIER.
///
/// Sellers ne connaît pas le service média ; sans cet événement, la ligne
/// disparaîtrait de la base et la pièce d'identité resterait dans le bucket privé
/// — plus référencée par rien, donc invisible du ménage de rétention. Une donnée
/// sensible qu'on croit supprimée et qui ne l'est pas est pire qu'une donnée
/// qu'on sait conserver.
/// </summary>
public sealed record KybDocumentRemovedDomainEvent(Guid SellerId, Guid UserId, Guid MediaId) : DomainEvent;
