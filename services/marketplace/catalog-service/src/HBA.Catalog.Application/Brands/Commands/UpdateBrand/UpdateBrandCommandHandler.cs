using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Brands;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Brands.Commands.UpdateBrand;

/// <summary>Charge la marque, vérifie l'unicité du slug si le nom change, met à jour puis persiste.</summary>
internal sealed class UpdateBrandCommandHandler : ICommandHandler<UpdateBrandCommand>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateBrandCommandHandler(IBrandRepository brandRepository, ICatalogUnitOfWork unitOfWork)
    {
        _brandRepository = brandRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _brandRepository.GetByIdAsync(new BrandId(command.BrandId), cancellationToken);
        if (brand is null)
        {
            return Result.Failure(Error.NotFound("catalog.brand.not_found", $"Marque {command.BrandId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        var newSlug = slugResult.Value.Value;
        if (newSlug != brand.Slug.Value && await _brandRepository.SlugExistsAsync(newSlug, cancellationToken))
        {
            return Result.Failure(Error.Conflict("catalog.brand.slug_taken", $"La marque « {newSlug} » existe déjà."));
        }

        var updateResult = brand.Update(command.Name, command.LogoUrl, command.Description);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
