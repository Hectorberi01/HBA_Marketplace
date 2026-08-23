using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Brands;

namespace HBA.Catalog.Application.Brands.Commands.DeleteBrand;

/// <summary>Charge la marque puis la supprime ; renvoie NotFound si absente.</summary>
internal sealed class DeleteBrandCommandHandler : ICommandHandler<DeleteBrandCommand>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public DeleteBrandCommandHandler(IBrandRepository brandRepository, ICatalogUnitOfWork unitOfWork)
    {
        _brandRepository = brandRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _brandRepository.GetByIdAsync(new BrandId(command.BrandId), cancellationToken);
        if (brand is null)
        {
            return Result.Failure(Error.NotFound("catalog.brand.not_found", $"Marque {command.BrandId} introuvable."));
        }

        _brandRepository.Remove(brand);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
