using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.UploadProductImage;

/// <summary>RATTACHE UNE IMAGE DÉJÀ DÉPOSÉE À UN PRODUIT.</summary>
public sealed record UploadProductImageCommand(
    Guid ProductId,
    Guid MediaId,
    string Url,
    string? AltText = null,
    bool IsPrimary = false) : ICommand<AttachedProductImage>;

/// <summary>Ce qu'un rattachement rend à son appelant.</summary>
public sealed record AttachedProductImage(Guid ProductMediaId, string Url);

internal sealed class UploadProductImageCommandHandler : ICommandHandler<UploadProductImageCommand, AttachedProductImage>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UploadProductImageCommandHandler(
        IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttachedProductImage>> Handle(UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        var added = product.AddMedia(
            command.MediaId, command.Url, ProductMediaType.Image, command.AltText, command.IsPrimary);
        if (added.IsFailure)
        {
            return Result.Failure<AttachedProductImage>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new AttachedProductImage(added.Value.Id, added.Value.Url));
    }
}
