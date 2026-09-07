using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.RemoveProductMedia;

/// <summary>Retire un média d'un produit.</summary>
public sealed record RemoveProductMediaCommand(Guid ProductId, Guid MediaId) : ICommand;

internal sealed class RemoveProductMediaCommandHandler : ICommandHandler<RemoveProductMediaCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public RemoveProductMediaCommandHandler(
        IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveProductMediaCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var removed = product.RemoveMedia(command.MediaId);
        if (removed.IsFailure)
        {
            return Result.Failure(removed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
