using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Offers;

/// <summary>Une offre, telle que la lit l'espace vendeur.</summary>
/// <param name="ProductName">Le libellé du produit vendu.</param>
/// <param name="Sku">La référence de la variante.</param>
/// <param name="CommissionAmount">Part plateforme figée au dernier calcul de prix.</param>
public sealed record OfferDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid VariantId,
    string? Sku,
    Guid StoreId,
    Guid SellerId,
    decimal SellerPrice,
    decimal BuyerPrice,
    decimal? PromotionalPrice,
    decimal EffectivePrice,
    DateTime? PromotionEndsOnUtc,
    decimal CommissionAmount,
    decimal ProviderFeeAmount,
    string Currency,
    string Status,
    string? StatusReason,
    string Condition,
    int HandlingTimeDays);

/// <summary>Les offres ACHETABLES d'un produit — la Buy Box.</summary>
public sealed record ListProductOffersQuery(Guid ProductId) : IQuery<IReadOnlyList<OfferDto>>;

/// <summary>Les offres d'une boutique, archivées exclues — « Mes mises en vente ».</summary>
public sealed record ListStoreOffersQuery(Guid StoreId) : IQuery<IReadOnlyList<OfferDto>>;

internal sealed class OfferQueryHandler
    : IQueryHandler<ListProductOffersQuery, IReadOnlyList<OfferDto>>,
      IQueryHandler<ListStoreOffersQuery, IReadOnlyList<OfferDto>>
{
    private readonly IProductOfferRepository _offers;
    private readonly IProductRepository _products;

    public OfferQueryHandler(IProductOfferRepository offers, IProductRepository products)
    {
        _offers = offers;
        _products = products;
    }

    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(ListProductOffersQuery query, CancellationToken ct)
        => Result.Success(await ProjectAsync(
            await _offers.ListActiveByProductAsync(query.ProductId, ct), ct));

    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(ListStoreOffersQuery query, CancellationToken ct)
        => Result.Success(await ProjectAsync(
            await _offers.ListByStoreAsync(query.StoreId, cancellationToken: ct), ct));

    /// <summary>
    /// Résout noms et SKU en DEUX requêtes, quelle que soit la taille de la liste.
    /// </summary>
    private async Task<IReadOnlyList<OfferDto>> ProjectAsync(
        IReadOnlyList<ProductOffer> offers, CancellationToken ct)
    {
        if (offers.Count == 0)
        {
            return [];
        }

        var noms = await _products.GetNamesByIdsAsync(
            offers.Select(o => o.ProductId).Distinct().ToList(), ct);

        var skus = await _products.GetSkusByVariantIdsAsync(
            offers.Select(o => o.VariantId).Distinct().ToList(), ct);

        return offers.Select(o => ToDto(o, noms, skus)).ToList();
    }

    private static OfferDto ToDto(
        ProductOffer o,
        IReadOnlyDictionary<Guid, string> noms,
        IReadOnlyDictionary<Guid, string> skus)
        => new(
            Id: o.Id.Value,
            ProductId: o.ProductId,

            // REPLI SUR UNE CHAÎNE VIDE, PAS SUR « Produit inconnu ».
            ProductName: noms.GetValueOrDefault(o.ProductId, string.Empty),

            VariantId: o.VariantId,
            Sku: skus.GetValueOrDefault(o.VariantId),
            StoreId: o.StoreId,
            SellerId: o.SellerId,
            SellerPrice: o.SellerPrice.Amount,
            BuyerPrice: o.BuyerPrice.Amount,
            PromotionalPrice: o.PromotionalPrice?.Amount,
            EffectivePrice: o.EffectivePrice.Amount,
            PromotionEndsOnUtc: o.PromotionEndsOnUtc,
            CommissionAmount: o.CommissionAmount,
            ProviderFeeAmount: o.ProviderFeeAmount,
            Currency: o.BuyerPrice.Currency,
            Status: o.Status.ToString(),
            StatusReason: o.StatusReason,
            Condition: o.Condition.ToString(),
            HandlingTimeDays: o.HandlingTimeDays);
}
