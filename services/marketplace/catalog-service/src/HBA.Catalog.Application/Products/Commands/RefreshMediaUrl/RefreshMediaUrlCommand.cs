using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.RefreshMediaUrl;

/// <summary>REMET À JOUR LA COPIE DE LECTURE D'UNE IMAGE.</summary>
public sealed record RefreshMediaUrlCommand(Guid ProductId, Guid MediaId, string Url) : ICommand;

internal sealed class RefreshMediaUrlCommandHandler : ICommandHandler<RefreshMediaUrlCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public RefreshMediaUrlCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RefreshMediaUrlCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);

        // UN PRODUIT DISPARU N'EST PAS UNE ERREUR ICI.
        if (product is null || !product.RefreshMediaUrl(command.MediaId, command.Url))
        {
            return Result.Success();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
