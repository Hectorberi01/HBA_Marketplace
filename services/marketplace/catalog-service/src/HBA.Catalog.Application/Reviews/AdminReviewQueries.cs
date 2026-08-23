using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Contracts;
using HBA.Catalog.Application.Products;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.Application.Reviews;

/// <summary>
/// La file de validation (§16 : <c>GET /products/reviews</c>).
///
/// ELLE NE SE LIT PAS DANS `product_reviews`.
///
/// Cette table journalise les décisions RENDUES. La file est faite des fiches qui
/// n'en ont pas encore — celles dont la révision courante est `PendingReview`.
/// Les confondre donnerait une file toujours vide, et personne ne comprendrait
/// pourquoi les soumissions n'y arrivent pas.
/// </summary>
public sealed record ListPendingReviewsQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<ProductSummary>>;

internal sealed class ListPendingReviewsQueryHandler
    : IQueryHandler<ListPendingReviewsQuery, PagedResult<ProductSummary>>
{
    private readonly IProductRepository _products;

    public ListPendingReviewsQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<PagedResult<ProductSummary>>> Handle(
        ListPendingReviewsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        var (items, total) = await _products.ListPendingReviewAsync(page, pageSize, cancellationToken);

        // VUE VENDEUR, ET C'EST TOUT L'OBJET DE L'ÉCRAN.
        //
        // L'administrateur doit lire ce qui ATTEND validation, c'est-à-dire la
        // révision courante. Projeter la révision publiée lui montrerait le contenu
        // déjà approuvé — donc exactement ce qu'il n'a pas à relire.
        var resumes = items.Select(ProductMapping.ToSellerSummary).ToList();

        return Result.Success(new PagedResult<ProductSummary>(resumes, total, page, pageSize));
    }
}

/// <summary>
/// L'historique des décisions sur une fiche (§16 : <c>GET /products/{id}/review</c>).
///
/// Rend TOUTES les décisions, pas seulement la dernière : c'est ce qui permet de
/// voir qu'une fiche a été rejetée trois fois pour le même motif avant d'être
/// approuvée — information qu'une seule ligne masquerait.
/// </summary>
public sealed record GetProductReviewsQuery(Guid ProductId) : IQuery<IReadOnlyList<ProductReviewSummary>>;

internal sealed class GetProductReviewsQueryHandler
    : IQueryHandler<GetProductReviewsQuery, IReadOnlyList<ProductReviewSummary>>
{
    private readonly IProductReviewRepository _reviews;

    public GetProductReviewsQueryHandler(IProductReviewRepository reviews) => _reviews = reviews;

    public async Task<Result<IReadOnlyList<ProductReviewSummary>>> Handle(
        GetProductReviewsQuery query, CancellationToken cancellationToken)
    {
        var decisions = await _reviews.ListByProductAsync(query.ProductId, cancellationToken);

        IReadOnlyList<ProductReviewSummary> resumes = decisions
            .Select(ReviewMapping.ToSummary)
            .ToList();

        // UNE LISTE VIDE N'EST PAS UNE ERREUR.
        //
        // Une fiche jamais soumise n'a aucune décision. Rendre 404 ferait croire à
        // l'administrateur que le produit n'existe pas, alors qu'il n'a simplement
        // pas d'historique.
        return Result.Success(resumes);
    }
}

/// <summary>Projection <see cref="ProductReview"/> → <see cref="ProductReviewSummary"/>.</summary>
public static class ReviewMapping
{
    public static ProductReviewSummary ToSummary(ProductReview review)
        => new(
            review.Id,
            review.ProductId,
            review.RevisionId,
            review.RevisionVersion,
            review.SellerId,
            review.ReviewedBy,
            review.Decision.ToString(),
            review.Comment,
            review.ReviewedAtUtc,
            review.Reasons
                .Select(m => new ProductReviewReasonSummary(m.Code, m.Field, m.Message))
                .ToList());
}
