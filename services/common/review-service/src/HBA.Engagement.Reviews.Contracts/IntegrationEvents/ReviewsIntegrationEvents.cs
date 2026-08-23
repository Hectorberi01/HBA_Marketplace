using HBA.Shared.IntegrationEvents;

namespace HBA.Engagement.Reviews.Contracts.IntegrationEvents;

/// <summary>
/// Un avis a été publié.
/// </summary>
/// <remarks>
/// `SellerId` A ÉTÉ AJOUTÉ, ET LE PRODUCTEUR L'AVAIT DÉJÀ EN MAIN.
///
/// Sans lui, `ReviewPublishedNotificationHandler` doit résoudre
/// `ProductId → SellerId` par un appel gRPC au catalogue : un aller-retour par
/// avis, pour une valeur que l'agrégat portait en clair. Le champ est là ; ce
/// détour peut disparaître quand quelqu'un touchera ce handler.
///
/// Le résumé d'origine disait « Consommé par Search » — il n'y a pas de service de
/// recherche dans ce dépôt. La ligne est retirée plutôt que recopiée.
/// </remarks>
public sealed record ReviewPublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ReviewId { get; init; }
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required int Rating { get; init; }
}

/// <summary>Un avis a été rejeté : sa contribution aux notes disparaît.</summary>
public sealed record ReviewRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ReviewId { get; init; }
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA NOTE D'UN VENDEUR VIENT D'ÊTRE RECALCULÉE. VOICI SA VALEUR.
///
/// CET ÉVÉNEMENT EXISTE POUR QUE `Seller.Rating` CESSE DE VALOIR ZÉRO.
///
/// La colonne existait, était persistée, et figurait dans la projection de
/// vitrine — mais `Seller.UpdateRating` n'avait AUCUN appelant dans tout le dépôt.
/// Toutes les boutiques étaient donc à `0/5`. Sur une place de marché, la preuve
/// sociale est alors constamment fausse, et elle l'est dans le sens qui décourage
/// l'achat.
///
/// Le dessein était pourtant écrit, dans `ReviewQueries.cs` : « recalculé
/// uniquement à la publication/au rejet d'un avis (par le module Sellers) […] la
/// note vendeur est ensuite persistée sur l'entité vendeur ». Seul le lien
/// manquait.
///
/// IL PORTE LA MOYENNE, PAS UN DELTA — ET C'EST CE QUI LE REND IDEMPOTENT.
///
/// review-service recalcule depuis SES tables (moyenne des avis `Published` du
/// vendeur) et publie le résultat. Recevoir deux fois le même message pose deux
/// fois la même valeur. Un delta, lui, double-compterait au premier rejeu — et
/// Kafka livre au moins une fois.
///
/// ET IL NE PORTE QUE `SellerId`, DÉLIBÉRÉMENT.
///
/// `KafkaEventNaming.AggregateId` choisit la clé de partition dans un ordre fixe,
/// où `ProductId` passe AVANT `SellerId`. Y ajouter le produit ferait partitionner
/// par PRODUIT : deux avis du même vendeur sur deux produits partiraient sur deux
/// partitions, arriveraient dans un ordre quelconque, et la dernière moyenne
/// écrite pourrait être la plus ancienne calculée. Sans produit, la clé est le
/// vendeur — et ses recalculs se suivent dans l'ordre, par construction.
///
/// C'est pour cette raison que ce n'est pas un champ de plus sur
/// `ReviewPublished`, ce qui aurait été plus court à écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// `Count` n'est persisté par personne aujourd'hui — `Seller` n'a pas de colonne
/// pour lui. Il voyage quand même : une note sans son nombre d'avis ne se lit pas
/// (4,9 sur deux avis n'est pas 4,9 sur trois cents), et l'ajouter plus tard sans
/// pouvoir republier l'historique coûterait bien davantage que de le porter dès
/// maintenant.
/// </remarks>
public sealed record SellerRatingRecomputedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }

    /// <summary>Moyenne des avis publiés, arrondie à deux décimales. `0` si aucun avis.</summary>
    public required double Average { get; init; }

    /// <summary>Nombre d'avis publiés retenus dans la moyenne.</summary>
    public required int Count { get; init; }
}
