using HBA.Shared.IntegrationEvents;

namespace HBA.Media.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ÉVÉNEMENTS DU SERVICE MÉDIA.
///
/// ILS N'EXISTAIENT PAS, ALORS QUE LE DOMAINE LES LEVAIT DÉJÀ.
///
/// `MediaAsset` lève `MediaReadyDomainEvent`, `MediaDeletedDomainEvent` et
/// `MediaProcessingFailedDomainEvent` depuis l'origine, et le commentaire de ces
/// événements affirmait qu'ils « passent par l'outbox transactionnel ». Aucun
/// gestionnaire ne les traduisait : les trois étaient levés, dispatchés dans le
/// processus, et mouraient là. Rien ne sortait vers Kafka.
///
/// La conséquence n'est pas théorique. Le §16 du cahier prévoit que les services
/// métier écoutent `media.ready` « pour mettre à jour leur état SANS COUPLAGE
/// HTTP PERMANENT ». Faute de publication, la seule façon pour catalog-service de
/// savoir qu'une miniature existe est d'interroger media-service à chaque
/// affichage — c'est-à-dire exactement la dépendance de disponibilité que
/// l'événement existe pour éviter : Media tombe, le catalogue tombe avec lui.
///
/// NOMS EN DEUX SEGMENTS, COMME LE CAHIER LES ÉCRIT.
///
/// `media.ready` et `media.deleted` sont les noms du §16. La forme littérale de
/// `[HbaEvent]` les reprend telle quelle plutôt que d'imposer
/// `media.asset.ready` : le contrat public doit ressembler à ce que les autres
/// équipes ont lu, pas à ce qu'une règle de nommage aurait préféré.
///
/// LE PROPRIÉTAIRE VOYAGE EN CHAÎNE DE CARACTÈRES, PAS EN ÉNUMÉRATION.
///
/// Un consommateur qui devrait référencer `MediaOwnerType` dépendrait du domaine
/// de Media — la frontière que ce service met un soin particulier à ne jamais
/// franchir. La chaîne coûte une comparaison ; l'énumération coûterait une
/// dépendance de compilation entre deux services.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[HbaEvent("media.ready", Version = 1, AggregateType = "MediaAsset")]
public sealed record MediaReadyIntegrationEvent : IntegrationEvent
{
    public required Guid MediaId { get; init; }

    /// <summary>« Product », « Seller », « Delivery »… — voir `MediaOwnerType`.</summary>
    public required string OwnerType { get; init; }

    public required Guid OwnerId { get; init; }

    /// <summary>La NATURE du fichier : « ProductImage », « DriverDocument »…</summary>
    public required string MediaType { get; init; }

    /// <summary>
    /// La clé de stockage de l'ORIGINAL.
    ///
    /// CE N'EST PAS UNE URL, ET SURTOUT PAS UNE URL SIGNÉE.
    ///
    /// Une URL signée porte une durée de vie de quelques minutes ; placée dans un
    /// événement conservé des jours par Kafka, elle serait périmée bien avant
    /// d'être lue, et un rejeu ne pourrait rien en faire. Le consommateur qui a
    /// besoin d'un accès demande une URL au moment où il en a besoin.
    /// </summary>
    public required string ObjectKey { get; init; }
}

/// <summary>
/// Le fichier est supprimé LOGIQUEMENT.
///
/// C'est le signal qui permet au service propriétaire d'oublier le `MediaId` :
/// une fiche produit qui garderait la référence afficherait une image morte
/// jusqu'à ce que quelqu'un s'en plaigne.
/// </summary>
[HbaEvent("media.deleted", Version = 1, AggregateType = "MediaAsset")]
public sealed record MediaDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid MediaId { get; init; }

    public required string OwnerType { get; init; }

    public required Guid OwnerId { get; init; }

    public required string MediaType { get; init; }
}

/// <summary>
/// La génération des variantes a échoué.
///
/// CE N'EST PAS « LE FICHIER EST PERDU », ET LE MALENTENDU COÛTERAIT UNE PHOTO.
///
/// L'original est intact dans le stockage et reste servable — `MediaAsset.IsUsable`
/// inclut délibérément l'état `Failed`. Seules les miniatures manquent. Un
/// consommateur qui traiterait cet événement comme une perte effacerait sa
/// référence à une image parfaitement valable, et le retraitement du §14
/// n'aurait plus rien à réparer.
///
/// La raison est transportée pour le diagnostic, jamais pour être affichée : elle
/// vient du générateur d'images et n'a pas été écrite pour un utilisateur.
/// </summary>
[HbaEvent("media.processing_failed", Version = 1, AggregateType = "MediaAsset")]
public sealed record MediaProcessingFailedIntegrationEvent : IntegrationEvent
{
    public required Guid MediaId { get; init; }

    public required string OwnerType { get; init; }

    public required Guid OwnerId { get; init; }

    public required string Reason { get; init; }
}
