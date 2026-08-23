using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Contracts.IntegrationEvents;

/// <summary>Un vendeur a été onboardé. Consommé par Notifications, Analytics…</summary>
public sealed record SellerRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public required string ShopName { get; init; }
}

/// <summary>
/// Un vendeur est devenu actif : il peut publier des produits. Consommé par
/// Catalog (autorisation), Notifications (e-mail de bienvenue vendeur)…
/// </summary>
public sealed record SellerActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Le vendeur a fermé son compte (suppression partielle). Consommé par Catalog
/// (retrait des produits de la vente), Notifications…
/// </summary>
public sealed record SellerClosedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Dossier KYB refusé. Consommé par Notifications, qui doit dire au vendeur CE
/// QU'IL DOIT CORRIGER — un refus sans motif n'est pas une décision de
/// modération, c'est une impasse.
///
/// La suspension du catalogue, quand le vendeur était actif, voyage séparément
/// dans SellerSuspendedIntegrationEvent : le refus et la sanction sont deux
/// faits distincts, même s'ils partent ensemble.
/// </summary>
public sealed record SellerKybRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Le vendeur a été suspendu par l'exploitation. Consommé par Products (retrait
/// du catalogue de la vente) et Notifications.
///
/// SUSPENSION N'EST PAS FERMETURE, malgré un effet voisin sur le catalogue.
/// La fermeture est demandée par le vendeur et se lève par une validation admin ;
/// la suspension est infligée et se lève par une décision d'exploitation. Les
/// confondre ferait qu'un vendeur sanctionné pourrait se rétablir seul.
/// </summary>
public sealed record SellerSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>La suspension a été levée : le catalogue retiré pour ce motif revient en vente.</summary>
public sealed record SellerSuspensionLiftedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>Le compte fermé du vendeur a été réactivé (validation admin).</summary>
public sealed record SellerReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Le vendeur est supprimé définitivement (admin). Consommé par Catalog (purge :
/// archivage de tous ses produits).
/// </summary>
public sealed record SellerDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Une boutique ferme — décision du vendeur ou de la plateforme, indistinctement.
///
/// UN SEUL ÉVÉNEMENT POUR LES DEUX. Le catalogue n'a pas à savoir POURQUOI :
/// dans les deux cas les offres de cette boutique quittent la vente. La
/// distinction vit dans le statut, qui décide qui a le droit de rouvrir.
/// </summary>
public sealed record StoreClosedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Une boutique rouvre : les offres retirées PAR CETTE FERMETURE reviennent en
/// vente. Celles qu'une suspension de vendeur ou un modérateur avait retirées
/// restent où elles sont — voir StoreCatalogClosure.
/// </summary>
public sealed record StoreOpenedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>
/// UNE PIÈCE KYB A ÉTÉ RETIRÉE DU DOSSIER.
///
/// SON SEUL CONSOMMATEUR EFFACE LE FICHIER, ET C'EST TOUTE SA RAISON D'ÊTRE.
///
/// Sellers ne connaît pas le service média. Sans ce message, la ligne
/// disparaîtrait de la base et la pièce d'identité resterait dans le bucket privé
/// — plus référencée par rien, donc invisible du ménage de rétention.
///
/// Il porte le SellerId en plus du MediaId : un message resté en souffrance doit
/// dire de QUI est le fichier qui résiste, pas seulement lequel.
/// </summary>
public sealed record KybDocumentRemovedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid MediaId { get; init; }
}

/// <summary>
/// Le dossier KYB est soumis à validation (§10.3 : `merchant.kyc.submitted`).
///
/// IL MANQUAIT, ET SON ABSENCE ÉTAIT ASYMÉTRIQUE.
///
/// Le service publiait le REFUS et rien d'autre du parcours KYB. Notifications ne
/// pouvait donc annoncer au vendeur que la mauvaise nouvelle, et l'exploitation
/// n'avait aucun signal pour alimenter sa file de validation.
///
/// Consommé par Notifications (accusé de dépôt au vendeur, alerte à la modération)
/// et par Analytics (délai de traitement des dossiers).
/// </summary>
public sealed record SellerKybSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>Ce que l'administrateur trouvera en ouvrant le dossier.</summary>
    public required int DocumentCount { get; init; }
}

/// <summary>
/// Le dossier KYB est validé (§10.3 : `merchant.kyc.approved`).
///
/// `SellerKybVerifiedDomainEvent` ÉTAIT LEVÉ ET N'AVAIT AUCUN GESTIONNAIRE.
///
/// L'événement de domaine existait depuis l'origine ; rien ne le traduisait en
/// événement d'intégration. Il partait donc dans le vide à chaque validation, et
/// le vendeur n'était prévenu que lorsqu'on lui refusait son dossier.
///
/// VALIDÉ N'EST PAS ACTIF. Ce sont deux faits distincts, et deux événements :
/// l'activation exige en plus des coordonnées de reversement, et c'est
/// `SellerActivatedIntegrationEvent` qui l'annonce.
/// </summary>
public sealed record SellerKybApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// La plateforme a suspendu une boutique (§10.3 : `outlet.status.changed`).
///
/// DISTINCT DE `StoreClosedIntegrationEvent`, ET C'EST TOUT L'INTÉRÊT.
///
/// Une sanction et des congés arrivaient jusqu'ici sous le même type. Le
/// consommateur ne pouvait les séparer qu'en lisant un motif en texte libre —
/// c'est-à-dire pas du tout, de façon fiable.
/// </summary>
public sealed record StoreSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// La sanction est levée. La boutique reste FERMÉE : c'est le vendeur qui rouvre.
///
/// Un consommateur qui a exclu cette boutique d'un classement doit la réintégrer
/// dans ses règles, sans pour autant la considérer comme ouverte.
/// </summary>
public sealed record StoreSuspensionLiftedIntegrationEvent : IntegrationEvent
{
    public required Guid StoreId { get; init; }
    public required Guid SellerId { get; init; }
}
