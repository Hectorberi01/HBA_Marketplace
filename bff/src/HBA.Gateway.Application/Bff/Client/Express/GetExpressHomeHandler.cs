using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Bff.Client.Food;
using HBA.Gateway.Application.Contracts.Catalog;

namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>Accueil HBAExpress.</summary>
public sealed class GetExpressHomeHandler
{
    public const string ScreenId = "client.express.home";

    /// <summary>Nombre de produits recommandés hydratés.</summary>
    public const int RecommendationCardCount = 8;

    private readonly ICatalogClient _catalog;
    private readonly IEngagementClient _engagement;
    private readonly IOrderClient _order;

    public GetExpressHomeHandler(
        ICatalogClient catalog, IEngagementClient engagement, IOrderClient order)
    {
        _catalog = catalog;
        _engagement = engagement;
        _order = order;
    }

    public async Task<BffEnvelope<ExpressHomeDto>> HandleAsync(CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        // ── Vague 1 : trois appels indépendants, lancés ensemble (§22) ───────
        var categoriesTask = context.CallAsync(
            "Catalog", () => _catalog.ListCategoriesAsync(cancellationToken));

        var recommendationsTask = context.CallAsync(
            "Engagement", () => _engagement.GetMyRecommendationsAsync(cancellationToken));

        var ordersTask = context.CallAsync(
            "Order", () => _order.ListMineAsync(cancellationToken));

        await Task.WhenAll(categoriesTask, recommendationsTask, ordersTask);

        var categories = context.Resolve(
            DependencyCriticality.Critical, "Catalog", await categoriesTask)!;

        var recommendations = context.Resolve(
            DependencyCriticality.Optional, "Engagement", await recommendationsTask);

        var orders = context.Resolve(
            DependencyCriticality.Optional, "Order", await ordersTask);

        // ── Vague 2 : hydratation des recommandations, en parallèle ──────────
        var recommendedIds = recommendations?.RecommendedProductIds
            .Take(RecommendationCardCount)
            .ToList() ?? [];

        var cardTasks = recommendedIds
            .Select(id => context.CallAsync(
                "Catalog", () => _catalog.GetProductAsync(id, cancellationToken)))
            .ToList();

        await Task.WhenAll(cardTasks);

        var cards = new List<ExpressProductCard>(cardTasks.Count);

        foreach (var task in cardTasks)
        {
            var result = await task;

            // UN PRODUIT RECOMMANDÉ INTROUVABLE EST SILENCIEUSEMENT OMIS.
            if (result is { IsSuccess: true, Value: CatalogProduct product })
            {
                cards.Add(ToCard(product));
            }
        }

        var activeOrder = orders?
            // LISTE PARTAGÉE AVEC L'ACCUEIL FOOD — cf.
            .Where(order => FoodOrderStatuses.IsActive(order.Status))
            .OrderByDescending(order => order.CreatedAtUtc)
            .Select(order => new ExpressActiveOrder(
                order.Id, order.Status, order.GrandTotal, order.Currency))
            .FirstOrDefault();

        var dto = new ExpressHomeDto(
            Categories:
            [
                .. categories
                    // Une catégorie non publiée n'a rien à faire sur un accueil
                    // public : le service rend TOUT, le filtrage est ici.
                    .Where(category => string.Equals(category.Status, "Published", StringComparison.OrdinalIgnoreCase))
                    .Select(category => new ExpressCategory(
                        category.Id, category.Name, category.Slug, category.ImageUrl)),
            ],
            RecommendedProducts: cards,
            ActiveOrder: activeOrder,
            FlashOffers: [],
            RecentlyViewed: []);

        return context.Complete(dto);
    }

    private static ExpressProductCard ToCard(CatalogProduct product)
        => new(
            product.Id,
            product.Name,
            product.Media.FirstOrDefault(media => media.IsPrimary)?.Url
                ?? product.Media.FirstOrDefault()?.Url);
}
