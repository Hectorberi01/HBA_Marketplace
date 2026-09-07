namespace HBA.Catalog.Contracts;

/// <param name="AttributeSchema">
/// Schéma d'attributs attendus, en JSON. <c> "{}"</c> si la catégorie n'impose
/// rien.
/// </param>
public sealed record CategorySummary(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    string Status,
    string? ImageUrl,
    string AttributeSchema);
