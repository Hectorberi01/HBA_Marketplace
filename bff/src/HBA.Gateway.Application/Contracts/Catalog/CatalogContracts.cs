namespace HBA.Gateway.Application.Contracts.Catalog;

/// <summary>Miroirs des contrats publics de <c>catalog-service</c>.</summary>
public sealed record CatalogProduct(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    string Slug,
    string Status,
    IReadOnlyList<CatalogProductVariant> Variants,
    IReadOnlyList<CatalogProductMedia> Media);

public sealed record CatalogProductVariant(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    int WeightGrams);

/// <summary>DEUX IDENTIFIANTS, ET LE SERVICE PRÉVIENT QU'ILS DIFFÈRENT.</summary>
public sealed record CatalogProductMedia(
    Guid Id,
    Guid MediaId,
    string Url,
    string Type,
    bool IsPrimary,
    int Position,
    string AltText);

public sealed record CatalogCategory(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    string Status,
    string? ImageUrl);
