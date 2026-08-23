using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

// ═════════════════════════════════════════════════════════════════════════════
// LES ÉVÉNEMENTS DU CYCLE DE VIE PRODUIT (§19).
//
// CEUX-CI PORTENT `[HbaEvent]`, CONTRAIREMENT AUX SIX ANCIENS DU MODULE.
//
// Les événements historiques de catalog (ProductCreated, BrandCreated…) n'ont
// pas l'attribut : ils suivent le nommage par service, sur le topic
// `service.catalog.v1`. Le §19 demande explicitement
// `hba.prod.catalog.product.v1` — c'est exactement ce que `[HbaEvent]` produit,
// `hba.<env>.<domaine>.<agrégat>.v<majeure>`.
//
// La bascule est possible sans casse parce qu'AUCUN de ces événements n'a de
// consommateur aujourd'hui : vérifié sur tout le dépôt avant d'écrire ce fichier.
// Sur un événement écouté, changer de topic aurait coupé le flux en silence — le
// producteur publie, le consommateur écoute ailleurs, et rien n'échoue.
//
// TOUS PORTENT `SellerId`, ET CE N'EST PAS DU REMPLISSAGE.
//
// Le premier consommateur attendu est la notification vendeur (« votre produit a
// été approuvé »). Sans le vendeur dans l'événement, chaque notification
// commencerait par un aller-retour vers catalog pour apprendre à qui écrire —
// c'est-à-dire un appel synchrone déclenché par un message asynchrone, exactement
// ce que le découplage cherchait à éviter.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Le vendeur a soumis une révision à validation (§15).</summary>
[HbaEvent("catalog", "product", "submitted", Version = 1, AggregateType = "Product")]
public sealed record ProductSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public required int RevisionVersion { get; init; }
}

/// <summary>
/// Un administrateur a validé la révision (§16).
///
/// APPROUVÉ N'EST PAS PUBLIÉ. Un consommateur qui rendrait la fiche visible
/// sur cet événement court-circuiterait la décision du vendeur — et publierait
/// des fiches préparées pour une date précise.
/// </summary>
[HbaEvent("catalog", "product", "approved", Version = 1, AggregateType = "Product")]
public sealed record ProductApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public required Guid ReviewedBy { get; init; }
}

/// <summary>
/// Rejet motivé (§16).
///
/// Les motifs ne voyagent pas ici : ils vivent dans ProductReview, un rejet en
/// porte plusieurs, chacun visant un champ. Les recopier dans l'événement en
/// ferait deux vérités à tenir d'accord — et c'est celle de l'événement qui
/// deviendrait fausse, personne ne pensant à la mettre à jour.
/// </summary>
[HbaEvent("catalog", "product", "rejected", Version = 1, AggregateType = "Product")]
public sealed record ProductRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public required Guid ReviewedBy { get; init; }
}

/// <summary>
/// La fiche est visible dans la marketplace.
///
/// `RevisionId` DÉSIGNE CE QUI EST DEVENU VISIBLE.
///
/// C'est ce qui permet à un moteur de recherche ou à un cache de vitrine de savoir
/// QUEL contenu indexer. Sans lui, une republication après correction serait
/// indiscernable d'une republication à l'identique, et l'index resterait sur
/// l'ancien texte — un défaut qui se manifeste par « la recherche ne trouve pas mon
/// produit sous son nouveau nom », des semaines plus tard.
/// </summary>
[HbaEvent("catalog", "product", "published", Version = 1, AggregateType = "Product")]
public sealed record ProductPublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid RevisionId { get; init; }
    public Guid? PreviousRevisionId { get; init; }
}

/// <summary>Retrait volontaire par le vendeur. Réversible.</summary>
[HbaEvent("catalog", "product", "unpublished", Version = 1, AggregateType = "Product")]
public sealed record ProductUnpublishedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>Retrait par la plateforme. Le vendeur ne peut pas le lever lui-même.</summary>
[HbaEvent("catalog", "product", "suspended", Version = 1, AggregateType = "Product")]
public sealed record ProductSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Suspension levée : la fiche revient à APPROVED, pas à PUBLISHED.
///
/// N'apparaît pas dans la liste du §19, et il le faut pourtant : sans lui, un
/// consommateur qui a masqué la fiche sur `suspended` n'apprend jamais qu'il peut
/// la reprendre — elle resterait invisible jusqu'à la republication du vendeur,
/// qui ne comprendrait pas pourquoi elle ne revient pas.
/// </summary>
[HbaEvent("catalog", "product", "restored", Version = 1, AggregateType = "Product")]
public sealed record ProductRestoredIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}

/// <summary>Retrait définitif du cycle courant. La ligne survit pour l'historique.</summary>
[HbaEvent("catalog", "product", "archived", Version = 1, AggregateType = "Product")]
public sealed record ProductArchivedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
}
