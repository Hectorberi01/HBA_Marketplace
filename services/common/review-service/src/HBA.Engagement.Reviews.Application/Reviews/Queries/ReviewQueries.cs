using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Engagement.Reviews.Contracts;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Application.Reviews.Queries;

/// <summary>Récupère un avis par son identifiant.</summary>
public sealed record GetReviewQuery(Guid ReviewId) : IQuery<ReviewSummary>;

/// <summary>Liste les avis publiés d'un produit.</summary>
public sealed record ListReviewsByProductQuery(Guid ProductId) : IQuery<IReadOnlyList<ReviewSummary>>;

/// <summary>Note agrégée d'un produit (moyenne + nombre d'avis publiés).</summary>
public sealed record GetProductRatingQuery(Guid ProductId) : IQuery<ProductRatingSummary>;

/// <summary>Note agrégée d'un vendeur (moyenne + nombre d'avis publiés sur ses produits).</summary>
public sealed record GetSellerRatingQuery(Guid SellerId) : IQuery<SellerRatingSummary>;

/// <summary>Liste tous les avis ciblant les produits d'un vendeur (back-office vendeur).</summary>
public sealed record ListReviewsBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<ReviewSummary>>;

/// <summary>La file de modération : une page d'avis, filtrable par statut.</summary>
/// <remarks>
/// SANS FILTRE, ELLE REND TOUT — Y COMPRIS LE PUBLIÉ.
///
/// Le cas courant est `Status = "Flagged"`, et c'est ce que l'écran demande par
/// défaut. Mais restreindre la requête aux seuls signalés interdirait de relire
/// un avis rejeté pour le restaurer, ce que `restore` permet précisément. Le
/// filtre est donc un paramètre, pas une règle.
/// </remarks>
public sealed record ListReviewsForModerationQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = null) : IQuery<PagedResult<ReviewSummary>>;

internal sealed class ListReviewsForModerationQueryHandler
    : IQueryHandler<ListReviewsForModerationQuery, PagedResult<ReviewSummary>>
{
    private readonly IReviewRepository _repository;

    public ListReviewsForModerationQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<PagedResult<ReviewSummary>>> Handle(
        ListReviewsForModerationQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // Un statut illisible est ignoré plutôt que refusé : la liste complète se
        // voit, un 400 sur une valeur mal orthographiée ne se comprend pas. Le
        // compte par statut rendu avec la page permet de vérifier ce qui a filtré.
        ReviewStatus? statut = Enum.TryParse<ReviewStatus>(query.Status, ignoreCase: true, out var lu)
            ? lu
            : null;

        var (items, total, comptes) = await _repository.ListForModerationAsync(
            page, pageSize, statut, cancellationToken);

        return new PagedResult<ReviewSummary>(
            items.Select(ReviewMapper.ToSummary).ToList(), total, page, pageSize, comptes);
    }
}

internal sealed class ListReviewsBySellerQueryHandler : IQueryHandler<ListReviewsBySellerQuery, IReadOnlyList<ReviewSummary>>
{
    private readonly IReviewRepository _repository;

    public ListReviewsBySellerQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ReviewSummary>>> Handle(ListReviewsBySellerQuery query, CancellationToken cancellationToken)
    {
        var reviews = await _repository.ListBySellerAsync(query.SellerId, cancellationToken: cancellationToken);
        IReadOnlyList<ReviewSummary> summaries = reviews.Select(ReviewMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetReviewQueryHandler : IQueryHandler<GetReviewQuery, ReviewSummary>
{
    private readonly IReviewRepository _repository;

    public GetReviewQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<ReviewSummary>> Handle(GetReviewQuery query, CancellationToken cancellationToken)
    {
        var review = await _repository.GetByIdAsync(new ReviewId(query.ReviewId), cancellationToken);
        return review is null
            ? Error.NotFound("reviews.not_found", "Avis introuvable.")
            : ReviewMapper.ToSummary(review);
    }
}

internal sealed class ListReviewsByProductQueryHandler : IQueryHandler<ListReviewsByProductQuery, IReadOnlyList<ReviewSummary>>
{
    private readonly IReviewRepository _repository;
    private readonly ICacheService _cache;

    public ListReviewsByProductQueryHandler(IReviewRepository repository, ICacheService cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<ReviewSummary>>> Handle(ListReviewsByProductQuery query, CancellationToken cancellationToken)
    {
        // Onglet « Avis » de la fiche produit : anonyme, et consulté autant que la
        // fiche elle-même.
        var summaries = await _cache.GetOrCreateAsync(
            ReviewsCacheKeys.ByProduct(query.ProductId),
            async ct =>
            {
                var reviews = await _repository.ListByProductAsync(query.ProductId, cancellationToken: ct);
                return reviews.Select(ReviewMapper.ToSummary).ToList();
            },
            ReviewsCacheKeys.RatingTtl,
            cancellationToken: cancellationToken);

        IReadOnlyList<ReviewSummary> result = summaries ?? [];
        return Result.Success(result);
    }
}

internal sealed class GetProductRatingQueryHandler : IQueryHandler<GetProductRatingQuery, ProductRatingSummary>
{
    private readonly IReviewRepository _repository;
    private readonly ICacheService _cache;

    public GetProductRatingQueryHandler(IReviewRepository repository, ICacheService cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<Result<ProductRatingSummary>> Handle(GetProductRatingQuery query, CancellationToken cancellationToken)
    {
        // L'AGRÉGATION la plus coûteuse de la fiche produit : moyenne + comptage sur
        // tous les avis. Son coût grandit avec le succès du produit — les fiches les
        // plus vues sont donc les plus chères à recalculer. Elle l'était à chaque
        // affichage, pour deux nombres qui ne changent qu'à la publication d'un avis.
        var summary = await _cache.GetOrCreateAsync(
            ReviewsCacheKeys.Rating(query.ProductId),
            async ct =>
            {
                var rating = await _repository.GetProductRatingAsync(query.ProductId, ct);
                return new ProductRatingSummary(rating.ProductId, rating.Average, rating.Count);
            },
            ReviewsCacheKeys.RatingTtl,
            cancellationToken: cancellationToken);

        // Un produit sans aucun avis est normal : note nulle, zéro avis.
        return summary ?? new ProductRatingSummary(query.ProductId, 0d, 0);
    }
}

internal sealed class GetSellerRatingQueryHandler : IQueryHandler<GetSellerRatingQuery, SellerRatingSummary>
{
    private readonly IReviewRepository _repository;

    public GetSellerRatingQueryHandler(IReviewRepository repository) => _repository = repository;

    public async Task<Result<SellerRatingSummary>> Handle(GetSellerRatingQuery query, CancellationToken cancellationToken)
    {
        // Pas de cache ici : recalculé uniquement à la publication/au rejet d'un avis
        // (par le module Sellers), pas à chaque affichage — la note vendeur est ensuite
        // persistée sur l'entité vendeur et lue directement depuis la vitrine.
        var rating = await _repository.GetSellerRatingAsync(query.SellerId, cancellationToken);
        return new SellerRatingSummary(rating.SellerId, rating.Average, rating.Count);
    }
}
