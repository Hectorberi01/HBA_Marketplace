using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Crée un produit en brouillon avec sa première révision, et renvoie son
/// identifiant (§14 : POST /api/v1/seller/catalog/products).
///
/// LE PRIX EST DEMANDÉ DÈS LA CRÉATION, LA DESCRIPTION NON.
///
/// C'est ce que fait l'exemple du §14, et c'est cohérent avec le §13 : le
/// formulaire vendeur a onze étapes, mais il n'envoie qu'une fois, à l'étape 11
/// (« Aperçu puis enregistrement / soumission »). Rendre le prix facultatif
/// permettrait d'enregistrer une fiche sans prix, que la soumission refuserait
/// ensuite sans que le vendeur sache à quelle étape il s'est arrêté.
///
/// La description, elle, reste facultative ici : le §23 ne l'exige qu'« avant
/// soumission », et l'exiger plus tôt ferait perdre une saisie en cours.
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
