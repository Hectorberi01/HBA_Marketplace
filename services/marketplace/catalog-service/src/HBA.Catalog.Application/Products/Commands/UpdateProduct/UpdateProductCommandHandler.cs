using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>
/// Charge le produit, reconstruit le contenu de la révision et le confie à
/// l'agrégat, qui décide seul s'il faut ouvrir une nouvelle version (§6).
/// </summary>
internal sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateProductCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var courante = product.CurrentRevision;

        var contenu = ContenuProduitFactory.Construire(
            command.Name,
            command.Description,
            command.CategoryId ?? courante.CategoryId,
            command.Tarification,
            command.Condition,
            command.ShortDescription,
            command.ProductType,
            command.BrandId ?? courante.BrandId,
            command.Attributes,
            command.Tags,
            // ═══════════════════════════════════════════════════════════════════
            // LE SLUG NE SUIT PLUS LE NOM. C'EST UN CHANGEMENT DE COMPORTEMENT.
            //
            // L'ancienne version le recalculait à chaque renommage. Sur une fiche
            // en brouillon, c'était sans conséquence. Avec les révisions, ça ne
            // l'est plus : publier la nouvelle version changerait l'URL PUBLIQUE
            // du produit (§17 : GET /products/{slug}).
            //
            // Ce que cela casse ne se voit nulle part dans le service. Les liens
            // déjà partagés — messages WhatsApp, publications, résultats de
            // recherche — rendent 404, et personne ne fait le rapprochement avec la
            // faute d'orthographe corrigée la veille.
            //
            // Le slug est donc figé à la création. Le changer devra être un geste
            // explicite, avec une redirection depuis l'ancien.
            // ═══════════════════════════════════════════════════════════════════
            courante.Slug,
            command.Specifications);

        if (contenu.IsFailure)
        {
            return Result.Failure(contenu.Error);
        }

        var updateResult = product.UpdateContenu(contenu.Value);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
