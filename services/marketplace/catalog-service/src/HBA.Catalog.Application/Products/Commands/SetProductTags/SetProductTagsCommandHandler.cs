using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.SetProductTags;

/// <summary>Charge le produit, remplace ses tags, persiste.</summary>
internal sealed class SetProductTagsCommandHandler : ICommandHandler<SetProductTagsCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public SetProductTagsCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetProductTagsCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        product.SetTags(command.Tags);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
