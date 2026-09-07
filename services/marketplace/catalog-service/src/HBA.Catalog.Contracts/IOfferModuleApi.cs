namespace HBA.Catalog.Contracts;

/// <summary>Une offre, telle que catalog-service la publie aux autres services.</summary>
/// <param name="Sku">La référence de la variante vendue.</param>
/// <param name="EffectivePrice">
/// Ce que l'acheteur paie AUJOURD'HUI. Calculé côté serveur — voir <c>
/// ProductOffer.EffectivePrice</c>, qui compare l'échéance de promotion à l'heure
/// courante.
/// </param>
public sealed record OfferSummary(
    Guid Id,
    Guid ProductId,
    Guid VariantId,
    Guid StoreId,
    Guid SellerId,
    string? Sku,
    decimal BuyerPrice,
    decimal? PromotionalPrice,
    decimal EffectivePrice,
    DateTime? PromotionEndsOnUtc,
    string Currency,
    string Status,
    bool IsPurchasable,
    string Condition,
    int HandlingTimeDays,
    Guid ShipFromLocationId);

/// <summary>Lecture des offres — le PRIX, servi par catalog-service.</summary>
public interface IOfferModuleApi
{
    Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default);

    /// <summary>Les offres achetables d'un produit — la Buy Box, triée par prix.</summary>
    Task<IReadOnlyList<OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Les offres qui vendent une référence donnée.</summary>
    Task<IReadOnlyList<OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default);
}
