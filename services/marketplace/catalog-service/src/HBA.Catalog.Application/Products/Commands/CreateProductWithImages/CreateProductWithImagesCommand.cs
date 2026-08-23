using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.CreateProductWithImages;

/// <summary>
/// Image DÉJÀ DÉPOSÉE dans le service média, fournie à la création du produit.
///
/// CE RECORD PORTAIT LES OCTETS. Il ne porte plus qu'une référence.
///
/// Le dépôt a lieu avant, à la frontière HTTP : la commande de création n'a plus
/// à réussir un appel réseau au milieu de son traitement. Un produit ne peut donc
/// plus échouer à se créer parce que le stockage était lent.
/// </summary>
public sealed record ProductImageUpload(Guid MediaId, string Url, string? AltText = null);

/// <summary>Résultat : id du produit créé + URLs publiques des images stockées.</summary>
public sealed record ProductWithImagesResult(Guid ProductId, IReadOnlyList<string> ImageUrls);

/// <summary>
/// Crée un produit (Draft) et téléverse ses images vers le service média externe,
/// puis associe les URLs renvoyées au produit (la première image devient
/// principale). Les uploads sont faits AVANT la persistance : si un upload échoue,
/// rien n'est créé.
/// </summary>
public sealed record CreateProductWithImagesCommand(
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Description,
    Guid? BrandId,
    string? Gtin,
    string? Ean,
    Guid? ProductGroupId,
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<ProductImageUpload> Images,
    // AJOUTÉS EN FIN DE LISTE, SANS VALEUR PAR DÉFAUT POUR LA TARIFICATION.
    //
    // Une révision ne peut pas exister sans prix de référence (§8, §23). Donner un
    // défaut ici — « 0 », « à définir » — créerait des fiches que la soumission
    // refuserait plus tard, sans que le vendeur sache pourquoi. Le compilateur
    // signale les appelants ; il n'y en a aucun aujourd'hui, cette commande n'étant
    // reliée à aucune route.
    TarificationSaisie Tarification = null!,
    ConditionSaisie? Condition = null,
    Guid? StoreId = null,
    string? ShortDescription = null,
    string? ProductType = null) : ICommand<ProductWithImagesResult>;

internal sealed class CreateProductWithImagesCommandHandler
    : ICommandHandler<CreateProductWithImagesCommand, ProductWithImagesResult>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateProductWithImagesCommandHandler(
        IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProductWithImagesResult>> Handle(
        CreateProductWithImagesCommand command, CancellationToken cancellationToken)
    {
        // 1) Création du produit. Slug UNIQUE résolu ici : deux produits homonymes
        //    (ou une reprise après un échec partiel de l'assistant) ne doivent pas
        //    être bloqués — on suffixe « -2 », « -3 »… au lieu de refuser.
        var slugResult = await SlugLibre.ResoudreAsync(_productRepository, command.Name, cancellationToken);
        if (slugResult.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(slugResult.Error);
        }

        var contenu = ContenuProduitFactory.Construire(
            command.Name,
            command.Description,
            command.CategoryId,
            command.Tarification,
            command.Condition,
            command.ShortDescription,
            command.ProductType,
            command.BrandId,
            command.Attributes,
            command.Tags,
            slugResult.Value);

        if (contenu.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(contenu.Error);
        }

        var result = Product.Create(
            command.SellerId, command.StoreId, contenu.Value,
            command.Gtin, command.Ean, command.ProductGroupId);
        if (result.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(result.Error);
        }

        var product = result.Value;

        // 2) Association des médias (la 1re image devient principale automatiquement).
        //
        // L'APPARTENANCE DES MÉDIAS EST VÉRIFIÉE PAR L'APPELANT, pas ici :
        // Catalog ne connaît pas le service média. Voir `Product.AddMedia`.
        foreach (var image in command.Images)
        {
            var add = product.AddMedia(
                image.MediaId, image.Url, ProductMediaType.Image,
                image.AltText ?? command.Name, isPrimary: false);
            if (add.IsFailure)
            {
                return Result.Failure<ProductWithImagesResult>(add.Error);
            }
        }

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProductWithImagesResult(product.Id.Value, command.Images.Select(i => i.Url).ToList());
    }

}
