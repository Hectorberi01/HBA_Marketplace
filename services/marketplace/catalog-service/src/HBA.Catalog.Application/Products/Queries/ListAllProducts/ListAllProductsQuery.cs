using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Queries.ListAllProducts;

/// <summary>Page de produits pour la console admin (recherche par nom, filtre statut).</summary>
public sealed record ListAllProductsQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Sort = null,
    string? Dir = null) : IQuery<PagedResult<ProductSummary>>;

internal sealed class ListAllProductsQueryHandler : IQueryHandler<ListAllProductsQuery, PagedResult<ProductSummary>>
{
    private readonly IProductRepository _productRepository;

    public ListAllProductsQueryHandler(IProductRepository productRepository) => _productRepository = productRepository;

    /// <summary>VOLONTAIREMENT NON MISE EN CACHE.</summary>
    public async Task<Result<PagedResult<ProductSummary>>> Handle(ListAllProductsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        ProductStatus? status = Enum.TryParse<ProductStatus>(query.Status, ignoreCase: true, out var parsed) ? parsed : null;
        bool desc = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        var (products, total, statusCounts) = await _productRepository.ListPagedAsync(page, pageSize, query.Search, status, query.Sort, desc, cancellationToken);
        var items = products.Select(ProductMapping.ToSellerSummary).ToList();
        return Result.Success(new PagedResult<ProductSummary>(items, total, page, pageSize, statusCounts));
    }
}
