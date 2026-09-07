using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Catalog;
using HBA.Gateway.Application.Contracts.Inventory;

namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>Fiche produit HBAExpress — le GABARIT que les autres agrégations reprennent.</summary>
public sealed class GetProductDetailHandler
{
    public const string ScreenId = "client.express.product_detail";

    /// <summary>Nombre maximal de SKU interrogés pour une fiche.</summary>
    public const int MaxStockLookups = 12;

    private readonly ICatalogClient _catalog;
    private readonly IInventoryClient _inventory;
    private readonly IEngagementClient _engagement;
    private readonly IMerchantClient _merchant;

    public GetProductDetailHandler(
        ICatalogClient catalog,
        IInventoryClient inventory,
        IEngagementClient engagement,
        IMerchantClient merchant)
    {
        _catalog = catalog;
        _inventory = inventory;
        _engagement = engagement;
        _merchant = merchant;
    }

    public async Task<BffEnvelope<ProductDetailDto>> HandleAsync(
        Guid productId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        // ── Vague 1 : le produit, seul.
        var product = context.Resolve(
            DependencyCriticality.Critical,
            "Catalog",
            await context.CallAsync("Catalog", () => _catalog.GetProductAsync(productId, cancellationToken)))!;

        // ── Vague 2 : stock (N appels), note, boutique — tous en parallèle ───
        var skus = product.Variants.Take(MaxStockLookups).ToList();

        var stockTasks = skus
            .Select(variant => context.CallAsync(
                "Inventory", () => _inventory.GetAvailabilityAsync(variant.Sku, cancellationToken)))
            .ToList();

        var ratingTask = context.CallAsync(
            "Engagement", () => _engagement.GetProductRatingAsync(productId, cancellationToken));

        // `SellerId` EST L'IDENTIFIANT DU VENDEUR, PAS DE LA BOUTIQUE.
        var storeTask = context.CallAsync(
            "Merchant", () => _merchant.GetStoreShowcaseAsync(product.SellerId, cancellationToken));

        // §22 — un seul point d'attente pour toute la vague.
        await Task.WhenAll(stockTasks.Cast<Task>().Append(ratingTask).Append(storeTask));

        // ── Assemblage ──────────────────────────────────────────────────────
        var stockBySku = new Dictionary<string, int>(StringComparer.Ordinal);
        var stockDegraded = false;

        for (var i = 0; i < skus.Count; i++)
        {
            var result = await stockTasks[i];

            if (result.IsSuccess && result.Value is StockAvailability availability)
            {
                stockBySku[skus[i].Sku] = availability.TotalAvailable;
                continue;
            }

            // Un seul avertissement pour N échecs de stock : le client n'a pas
            // besoin de douze lignes identiques, il a besoin de savoir que le stock
            // n'est pas fiable sur cette fiche.
            stockDegraded = true;
        }

        if (stockDegraded)
        {
            context.Resolve(
                DependencyCriticality.Important,
                "Inventory",
                ServiceResult<StockAvailability>.Failure(502, "stock partiellement indisponible"));
        }

        var rating = context.Resolve(DependencyCriticality.Optional, "Engagement", await ratingTask);
        var store = context.Resolve(DependencyCriticality.Important, "Merchant", await storeTask);

        var dto = new ProductDetailDto(
            new ProductDetailProduct(
                product.Id,
                product.SellerId,
                product.CategoryId,
                product.BrandId,
                product.Name,
                product.Description,
                product.Status),
            [
                .. product.Variants.Select(variant => new ProductDetailVariant(
                    variant.Id,
                    variant.Sku,
                    variant.Attributes,
                    // `TryGetValue` : une déclinaison au-delà du plafond, ou dont
                    // l'appel a échoué, reste à `null` — « inconnu », pas « zéro ».
                    stockBySku.TryGetValue(variant.Sku, out var available) ? available : null)),
            ],
            [
                .. product.Media
                    .OrderByDescending(media => media.IsPrimary)
                    .ThenBy(media => media.Position)
                    .Select(media => new ProductDetailMedia(
                        media.MediaId, media.Url, media.IsPrimary, media.AltText)),
            ],
            rating is null ? null : new ProductDetailRating(rating.Average, rating.Count),
            store is null ? null : new ProductDetailStore(store.Id, store.Name, store.LogoUrl, store.IsSelling),
            ProductDetailDelivery.NotEvaluated);

        return context.Complete(dto);
    }
}
