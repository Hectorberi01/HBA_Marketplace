using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Queries.PublicCatalog;

// LA VITRINE (§17) — LES SEULES REQUÊTES QU'UNE ROUTE ANONYME PEUT APPELER.

/// <summary>
/// Recherche de vitrine (§17 : query, categoryId, brandId, sellerId, condition,
/// prix, tri, pagination).
/// </summary>
public sealed record SearchPublicProductsQuery(
    string? Query = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? SellerId = null,
    string? Condition = null,
    long? MinPrice = null,
    long? MaxPrice = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<ProductSummary>>;

internal sealed class SearchPublicProductsQueryHandler
    : IQueryHandler<SearchPublicProductsQuery, PagedResult<ProductSummary>>
{
    private readonly IProductRepository _products;

    public SearchPublicProductsQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<PagedResult<ProductSummary>>> Handle(
        SearchPublicProductsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // UN ÉTAT COMMERCIAL INCONNU EST IGNORÉ, PAS REFUSÉ.
        ProductConditionType? condition = null;
        if (!string.IsNullOrWhiteSpace(query.Condition)
            && Enum.TryParse<ProductConditionType>(
                query.Condition.Replace("_", string.Empty), ignoreCase: true, out var analysee)
            && Enum.IsDefined(typeof(ProductConditionType), analysee))
        {
            condition = analysee;
        }

        var (items, total) = await _products.SearchPublishedAsync(
            new RecherchePublique(
                query.Query,
                query.CategoryId,
                query.BrandId,
                query.SellerId,
                condition,
                query.MinPrice,
                query.MaxPrice,
                query.Sort,
                page,
                pageSize),
            cancellationToken);

        var resumes = items
            .Select(ProductMapping.ToPublicSummary)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        // PAS DE FACETTES ICI.
        return Result.Success(new PagedResult<ProductSummary>(resumes, total, page, pageSize));
    }
}

/// <summary>Fiche publique par slug (§17 : <c>GET /products/{slug}</c>).</summary>
public sealed record GetPublicProductBySlugQuery(string Slug) : IQuery<ProductSummary>;

internal sealed class GetPublicProductBySlugQueryHandler
    : IQueryHandler<GetPublicProductBySlugQuery, ProductSummary>
{
    private readonly IProductRepository _products;

    public GetPublicProductBySlugQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<ProductSummary>> Handle(
        GetPublicProductBySlugQuery query, CancellationToken cancellationToken)
    {
        var product = await _products.GetPublishedBySlugAsync(query.Slug, cancellationToken);
        var resume = product is null ? null : ProductMapping.ToPublicSummary(product);

        if (resume is null)
        {
            return Error.NotFound("catalog.product.not_found", "Ce produit n'existe pas ou n'est plus en vente.");
        }

        return resume;
    }
}

/// <summary>Fiche publique par identifiant.</summary>
public sealed record GetPublicProductQuery(Guid ProductId) : IQuery<ProductSummary>;

internal sealed class GetPublicProductQueryHandler
    : IQueryHandler<GetPublicProductQuery, ProductSummary>
{
    private readonly IProductRepository _products;

    public GetPublicProductQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<ProductSummary>> Handle(
        GetPublicProductQuery query, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(new ProductId(query.ProductId), cancellationToken);
        var resume = product is null ? null : ProductMapping.ToPublicSummary(product);

        // MÊME MESSAGE POUR « N'EXISTE PAS » ET « PAS PUBLIÉ ».
        if (resume is null)
        {
            return Error.NotFound("catalog.product.not_found", "Ce produit n'existe pas ou n'est plus en vente.");
        }

        return resume;
    }
}
