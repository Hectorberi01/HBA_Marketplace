using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

// ═════════════════════════════════════════════════════════════════════════════
// LE RÉFÉRENTIEL DES MARQUES ET LA FILE DES DEMANDES (§10, §16).
//
// Deux objets distincts, et c'est tout le mécanisme : le vendeur ne crée pas de
// marque, il en DEMANDE une. L'administrateur tranche entre créer une marque de
// plus et rattacher la demande à celle qui existe déjà — « samsumg » vers
// « Samsung ». Sans ce second geste, le dispositif produirait exactement les
// doublons qu'il devait empêcher.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Une marque du référentiel, telle que `BrandSummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA LISTE EST SERVIE PAR LA ROUTE PUBLIQUE, PAS PAR LA ROUTE ADMIN.
///
/// `GET /api/v1/catalog/brands` est déclarée sur le groupe public et marquée
/// `AllowAnonymous` ; le groupe `admin` ne porte QUE les écritures — création,
/// modification, publication, suppression — et la file des demandes. Il n'y a
/// donc rien à ouvrir côté serveur : il faut seulement savoir que la lecture ne
/// se trouve pas là où les écritures se trouvent.
///
/// CONSÉQUENCE POUR L'ÉCRAN : la liste rend TOUTES les marques, y compris les
/// `Pending` et les `Archived`. `ListBrandsQueryHandler` appelle `ListAllAsync`
/// sans filtre — le commentaire du service dit d'ailleurs « back-office admin ».
/// Ce n'est pas la vitrine, et l'écran n'a pas à re-filtrer pour compenser.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record MarqueAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("logoUrl")] string? LogoUrl,
    [property: JsonPropertyName("description")] string? Description);

/// <summary>Une demande de marque en attente, telle que `BrandRequestSummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA FILE NE CONTIENT QUE DES DEMANDES `Pending`, ET IL N'Y A PAS D'HISTORIQUE.
///
/// `ListPendingAsync` filtre sur `Status == Pending` et trie par date de demande
/// croissante. Les champs `RejectionReason`, `BrandId` et `ReviewedAtUtc`
/// existent dans le contrat mais arrivent donc TOUJOURS nuls ici : ils décrivent
/// une demande déjà tranchée, et une demande tranchée quitte la file.
///
/// L'écran ne peut par conséquent PAS montrer « ce qui a été refusé la semaine
/// dernière ». Il faudrait pour cela une route de liste acceptant un statut, qui
/// n'existe pas. Ne pas confondre avec un filtre à ajouter côté client : la
/// donnée n'est pas envoyée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record DemandeMarqueAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("brandId")] Guid? BrandId,
    [property: JsonPropertyName("rejectionReason")] string? RejectionReason,
    [property: JsonPropertyName("requestedAtUtc")] DateTimeOffset RequestedAtUtc,
    [property: JsonPropertyName("reviewedAtUtc")] DateTimeOffset? ReviewedAtUtc);

/// <summary>Ce que l'approbation d'une demande rend : l'identifiant de la marque retenue.</summary>
/// <remarks>
/// C'est la marque CRÉÉE, ou la marque EXISTANTE à laquelle la demande vient
/// d'être rattachée — le serveur rend le même champ dans les deux cas. L'écran
/// s'en sert pour sélectionner la marque concernée après rechargement, afin que
/// l'administrateur voie immédiatement sur quoi son geste a porté.
/// </remarks>
public sealed record ReponseApprobation(
    [property: JsonPropertyName("brandId")] Guid BrandId);
