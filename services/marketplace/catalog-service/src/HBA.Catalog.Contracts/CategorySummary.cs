namespace HBA.Catalog.Contracts;

/// <param name="AttributeSchema">
/// Schéma d'attributs attendus, en JSON. <c>"{}"</c> si la catégorie n'impose
/// rien.
///
/// AJOUTÉ AU CONTRAT PARCE QUE PERSONNE NE POUVAIT LE LIRE.
///
/// La colonne existait depuis le premier jour côté Catalog, mais l'API
/// publique ne l'exposait pas : aucun module ne pouvait donc valider quoi que
/// ce soit contre elle. C'est le chaînon manquant du flux décrit au cahier —
/// Product → CategoryId → Catalog → AttributeSchema → Validation.
///
/// Catalog reste PROPRIÉTAIRE du schéma : il le stocke et le fait éditer. Le
/// module Products en est LECTEUR : il l'interprète pour refuser un produit
/// mal renseigné.
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
