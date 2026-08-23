using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

// ═════════════════════════════════════════════════════════════════════════════
// L'ARBRE DES CATÉGORIES ET LE RÉFÉRENTIEL D'ATTRIBUTS (§10, §13, §23).
//
// Ces deux objets décident de ce qu'un vendeur peut mettre en vente et de ce que
// son formulaire lui demande. Un attribut marqué « requis » sur une catégorie
// bloque immédiatement toute nouvelle soumission dans cette catégorie — ce n'est
// pas un réglage d'affichage.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Une catégorie, telle que `CategorySummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `Path` EST LA VRAIE STRUCTURE DE L'ARBRE, PAS `ParentId`.
///
/// Le chemin matérialisé — « /animaux/chiens/alimentation » — porte l'index
/// UNIQUE de la table, et c'est LUI que `ListDescendantsAsync` interroge pour
/// propager une publication : `Path.StartsWith(chemin + "/")`. `ParentId` n'est
/// qu'une propriété indexée, SANS clé étrangère vers `categories`.
///
/// Les deux peuvent donc diverger, et l'écran le détecte : voir
/// `CategoriesViewModel`, qui compare le chemin de chaque nœud à celui de son
/// parent déclaré.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="AttributeSchema">
/// Colonne `jsonb` — un JSON mal formé est refusé PAR LA BASE, donc en 500 et non
/// en 400 : ni `CreateCategoryCommandValidator` ni `UpdateCategoryCommandValidator`
/// ne le vérifient. L'écran valide donc la forme avant d'envoyer.
/// </param>
public sealed record CategorieAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("parentId")] Guid? ParentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("imageUrl")] string? ImageUrl,
    [property: JsonPropertyName("attributeSchema")] string? AttributeSchema);

/// <summary>Une définition d'attribut réutilisable, telle que `AttributeDefinitionSummary` la rend.</summary>
public sealed record DefinitionAttribut(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("options")] IReadOnlyList<string>? Options);

/// <summary>Un attribut TEL QUE LA CATÉGORIE L'EXIGE, `CategoryAttributeSummary`.</summary>
/// <remarks>
/// LE LIBELLÉ ET LE TYPE VIENNENT DE LA DÉFINITION ; `Required`, `Variant` ET
/// `DisplayOrder` VIENNENT DU RATTACHEMENT.
///
/// C'est pourquoi rattacher deux fois le même attribut n'est pas un conflit :
/// le service met à jour les trois réglages. Une console renvoie l'état complet
/// du formulaire à chaque enregistrement, et le serveur l'a prévu.
/// </remarks>
public sealed record AttributCategorie(
    [property: JsonPropertyName("attributeDefinitionId")] Guid AttributeDefinitionId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("options")] IReadOnlyList<string>? Options,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("variant")] bool Variant,
    [property: JsonPropertyName("displayOrder")] int DisplayOrder);

/// <summary>Ce que la publication en cascade rend : le nombre de catégories basculées.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COMPTE PEUT ÊTRE INFÉRIEUR À CE QUE L'ON ATTENDAIT, ET C'EST VOULU.
///
/// `PublishCategoryCommandHandler` SAUTE les descendants archivés au lieu
/// d'échouer : propager l'échec ferait avorter l'opération entière à cause d'une
/// branche retirée volontairement du catalogue. Le compteur est le seul moyen de
/// constater l'écart — l'écran l'affiche donc tel quel.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ReponseCascade(
    [property: JsonPropertyName("affected")] int Affected);
