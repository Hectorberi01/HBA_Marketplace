using HBA.Shared.Domain.Events;

namespace HBA.Catalog.Domain.Products.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES HUIT FAITS DU CYCLE DE VIE (§19).
///
/// ILS REMPLACENT UN ÉVÉNEMENT UNIQUE PORTANT LE NOM DU STATUT.
///
/// Le domaine ne levait que `ProductStatusChangedDomainEvent(ProductId, Status)`,
/// traduit en un `ProductStatusChangedIntegrationEvent` publié sur Kafka. Deux
/// choses le condamnaient :
///
///   • le §19 demande huit sujets distincts, parce qu'un consommateur qui ne
///     s'intéresse qu'aux publications ne doit pas filtrer huit fois plus de
///     messages qu'il n'en traite ;
///   • surtout, chaque transition porte des données DIFFÉRENTES. Une approbation
///     nomme l'administrateur, un rejet porte des motifs, une publication désigne
///     la révision devenue visible. Un événement à deux champs ne pouvait rien
///     dire de tout cela — et les consommateurs devaient rappeler l'API pour
///     apprendre ce que l'événement aurait dû leur donner.
///
/// ET L'ANCIEN N'AVAIT AUCUN CONSOMMATEUR.
///
/// Vérifié avant de le retirer : `ProductStatusChangedIntegrationEvent` n'était lu
/// nulle part dans le dépôt. Il était publié à chaque changement de statut, sur
/// chaque produit, et personne n'écoutait — exactement ce que
/// le contrôle `event-consumers` existe pour rendre visible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ProductSubmittedForReviewDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    int RevisionVersion) : DomainEvent;

public sealed record ProductApprovedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    Guid ReviewedBy) : DomainEvent;

/// <summary>
/// Les motifs ne voyagent pas ici : ils vivent dans ProductReview, et un rejet en
/// porte plusieurs, chacun avec un champ visé (§16). Les recopier dans
/// l'événement en ferait deux vérités à tenir d'accord.
/// </summary>
public sealed record ProductRejectedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    Guid ReviewedBy) : DomainEvent;

/// <summary>
/// CET ÉVÉNEMENT NOMME LA RÉVISION, PAS SEULEMENT LE PRODUIT.
///
/// C'est ce qui permet à un consommateur — moteur de recherche, cache de vitrine —
/// de savoir QUEL contenu est devenu visible. Sans <c>RevisionId</c>, une
/// republication après correction serait indiscernable d'une republication à
/// l'identique, et l'index resterait sur l'ancien texte.
/// </summary>
public sealed record ProductPublishedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid RevisionId,
    Guid? PreviousRevisionId) : DomainEvent;

public sealed record ProductUnpublishedDomainEvent(
    Guid ProductId,
    Guid SellerId) : DomainEvent;

/// <summary>Retrait par la plateforme. La raison est destinée au vendeur.</summary>
public sealed record ProductSuspendedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    string? Reason) : DomainEvent;

public sealed record ProductRestoredDomainEvent(
    Guid ProductId,
    Guid SellerId) : DomainEvent;

public sealed record ProductArchivedDomainEvent(
    Guid ProductId,
    Guid SellerId) : DomainEvent;

/// <summary>
/// Une nouvelle révision est ouverte sur un produit DÉJÀ PUBLIÉ (§6).
///
/// Ce fait n'a pas de sujet Kafka au §19, et c'est volontaire : rien à l'extérieur
/// n'a à réagir tant que la révision n'est pas publiée — la marketplace continue
/// de servir l'ancienne. Il reste utile en interne, pour l'audit et pour la file
/// de validation.
/// </summary>
public sealed record ProductRevisionOpenedDomainEvent(
    Guid ProductId,
    Guid RevisionId,
    int RevisionVersion,
    Guid? PublishedRevisionId) : DomainEvent;
