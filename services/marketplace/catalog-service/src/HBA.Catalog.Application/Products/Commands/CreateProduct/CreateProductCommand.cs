using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Crée un produit en brouillon avec sa première révision, et renvoie son
/// identifiant (§14 : POST /api/v1/seller/catalog/products).
/// </summary>
public sealed record CreateProductCommand(
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Description,
    TarificationSaisie Tarification,
    Guid? StoreId = null,
    ConditionSaisie? Condition = null,
    string? ShortDescription = null,
    string? ProductType = null,
    Guid? BrandId = null,
    string? Gtin = null,
    string? Ean = null,
    Guid? ProductGroupId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<GroupeSpecSaisi>? Specifications = null) : ICommand<Guid>;
