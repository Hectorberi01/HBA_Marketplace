using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Categories;

namespace HBA.Catalog.Application.Categories.Queries.GetCategory;

internal sealed class GetCategoryQueryHandler : IQueryHandler<GetCategoryQuery, CategorySummary>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICacheService _cache;

    public GetCategoryQueryHandler(ICategoryRepository categoryRepository, ICacheService cache)
    {
        _categoryRepository = categoryRepository;
        _cache = cache;
    }

    public async Task<Result<CategorySummary>> Handle(GetCategoryQuery query, CancellationToken cancellationToken)
    {
        // Même clé que CatalogModuleApi.GetCategoryAsync : une seule entrée pour les
        // deux chemins de lecture.
        var summary = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.Category(query.CategoryId),
            async ct =>
            {
                var category = await _categoryRepository.GetByIdAsync(new CategoryId(query.CategoryId), ct);
                return category is null
                    ? null
                    : new CategorySummary(
                        category.Id.Value,
                        category.ParentId,
                        category.Name,
                        category.Slug.Value,
                        category.Path,
                        category.Status.ToString(),
                        category.ImageUrl,
                        category.AttributeSchema);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

        if (summary is null)
        {
            return Error.NotFound("catalog.category.not_found", $"Catégorie {query.CategoryId} introuvable.");
        }

        return summary;
    }
}
