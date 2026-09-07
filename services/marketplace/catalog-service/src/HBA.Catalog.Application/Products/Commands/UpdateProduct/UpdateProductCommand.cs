using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>Met à jour le contenu descriptif d'un produit (§14 : PUT /products/{id}).</summary>
public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string Description,
    TarificationSaisie Tarification,
    ConditionSaisie? Condition = null,
    string? ShortDescription = null,
    string? ProductType = null,
    Guid? BrandId = null,
    Guid? CategoryId = null,
    string? Gtin = null,
    string? Ean = null,
    Guid? ProductGroupId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<GroupeSpecSaisi>? Specifications = null) : ICommand;
