using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.CreateProduct;

internal sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateProductCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        // Slug UNIQUE résolu ici : deux produits homonymes (ou une reprise après un
        // échec partiel) ne doivent pas bloquer la création — on suffixe « -2 », « -3 »…
        var slugResult = await SlugLibre.ResoudreAsync(_productRepository, command.Name, cancellationToken);
        if (slugResult.IsFailure)
        {
            return Result.Failure<Guid>(slugResult.Error);
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
            slugResult.Value,
            command.Specifications);

        if (contenu.IsFailure)
        {
            return Result.Failure<Guid>(contenu.Error);
        }

        var result = Product.Create(
            command.SellerId,
            command.StoreId,
            contenu.Value,
            command.Gtin,
            command.Ean,
            command.ProductGroupId);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var product = result.Value;

        await _productRepository.AddAsync(product, cancellationToken);

        // SaveChanges dispatche le ProductCreatedDomainEvent, dont le handler
        // écrit l'IntegrationEvent dans l'outbox — le tout dans une transaction.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Id.Value;
    }

}
