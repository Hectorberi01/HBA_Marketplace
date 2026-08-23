using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>
/// Met à jour le contenu descriptif d'un produit (§14 : PUT /products/{id}).
///
/// CETTE COMMANDE PEUT OUVRIR UNE NOUVELLE RÉVISION SANS LE DIRE.
///
/// C'est l'agrégat qui décide (§6) : réécriture en place si la fiche est encore
/// éditable ou si le changement n'est pas critique, nouvelle révision sinon. Le
/// handler ne choisit pas — s'il choisissait, il faudrait exposer deux routes et
/// que le client sache laquelle appeler, ce qui reviendrait à confier la règle du
/// §6 au frontend.
/// </summary>
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
