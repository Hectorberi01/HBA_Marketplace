using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Categories;

namespace HBA.Catalog.Application.Categories.Commands.PublishCategory;

/// <summary>Charge la catégorie et applique la transition de publication du domaine.</summary>
internal sealed class PublishCategoryCommandHandler : ICommandHandler<PublishCategoryCommand, int>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public PublishCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(PublishCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure<int>(
                Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var result = category.Publish();
        if (result.IsFailure)
        {
            return Result.Failure<int>(result.Error);
        }

        var published = 1;

        if (command.IncludeDescendants)
        {
            var descendants = await _categoryRepository.ListDescendantsAsync(category.Path, cancellationToken);

            foreach (var descendant in descendants)
            {
                // LES DESCENDANTS ARCHIVÉS SONT IGNORÉS, PAS RESSUSCITÉS.
                if (descendant.Publish().IsFailure)
                {
                    continue;
                }

                published++;
            }
        }

        // Une seule transaction : soit la branche entière bascule, soit rien.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return published;
    }
}
