namespace HBA.Catalog.Contracts;

/// <summary>Une définition d'attribut, réutilisable entre catégories (§10).</summary>
public sealed record AttributeDefinitionSummary(
    Guid Id,
    string Code,
    string Name,
    string Type,
    string? Unit,
    IReadOnlyList<string> Options);

/// <summary>
/// Un attribut TEL QUE LA CATÉGORIE LE DEMANDE (§10, §13 étape 8).
///
/// C'est ce que le formulaire vendeur consomme pour construire ses champs
/// dynamiques : le libellé et le type viennent de la définition, `Required`,
/// `Variant` et `DisplayOrder` du rattachement.
/// </summary>
public sealed record CategoryAttributeSummary(
    Guid AttributeDefinitionId,
    string Code,
    string Name,
    string Type,
    string? Unit,
    IReadOnlyList<string> Options,
    bool Required,
    bool Variant,
    int DisplayOrder);

/// <summary>Une demande de marque (§10, §16).</summary>
public sealed record BrandRequestSummary(
    Guid Id,
    Guid SellerId,
    string Name,
    string? Note,
    string Status,
    Guid? BrandId,
    string? RejectionReason,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReviewedAtUtc);
