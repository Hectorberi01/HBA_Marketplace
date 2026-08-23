using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Categories;

namespace HBA.Catalog.Application.Categories.Commands.CreateCategory;

internal sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Le chemin matérialisé se construit à partir du parent : on le charge.
        string? parentPath = null;
        if (command.ParentId is { } parentId && parentId != Guid.Empty)
        {
            var parent = await _categoryRepository.GetByIdAsync(new CategoryId(parentId), cancellationToken);
            if (parent is null)
            {
                return Error.NotFound("catalog.category.parent_not_found", $"Catégorie parente {parentId} introuvable.");
            }

            parentPath = parent.Path;
        }

        var result = Category.Create(command.Name, command.ParentId, parentPath, command.ImageUrl, command.AttributeSchema);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var category = result.Value;

        // Unicité par CHEMIN, pas par slug : « Alimentation » peut exister sous
        // « Chiens » et sous « Chats », mais pas deux fois sous le même parent.
        if (await _categoryRepository.PathExistsAsync(category.Path, excludeId: null, cancellationToken))
        {
            return Error.Conflict(
                "catalog.category.path_taken",
                $"Une catégorie « {category.Name} » existe déjà à cet emplacement.");
        }

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return category.Id.Value;
    }
}
