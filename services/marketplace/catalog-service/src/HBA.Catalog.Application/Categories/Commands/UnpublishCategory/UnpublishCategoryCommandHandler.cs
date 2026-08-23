using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Categories;

namespace HBA.Catalog.Application.Categories.Commands.UnpublishCategory;

/// <summary>Charge la catégorie et applique la transition de dépublication du domaine.</summary>
internal sealed class UnpublishCategoryCommandHandler : ICommandHandler<UnpublishCategoryCommand, int>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UnpublishCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(UnpublishCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure<int>(
                Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var result = category.Unpublish();
        if (result.IsFailure)
        {
            return Result.Failure<int>(result.Error);
        }

        var unpublished = 1;

        if (command.IncludeDescendants)
        {
            var descendants = await _categoryRepository.ListDescendantsAsync(category.Path, cancellationToken);

            foreach (var descendant in descendants)
            {
                // Les archivées sont déjà hors de l'arbre visible : les « dépublier »
                // n'aurait aucun sens et `Unpublish()` les refuse. On les saute.
                if (descendant.Unpublish().IsFailure)
                {
                    continue;
                }

                unpublished++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return unpublished;
    }
}
