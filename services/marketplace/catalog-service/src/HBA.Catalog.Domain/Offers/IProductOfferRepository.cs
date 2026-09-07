namespace HBA.Catalog.Domain.Offers;

/// <summary>
/// Accès aux offres. Agrégat séparé — voir l'encadré de <see cref="ProductOffer"/>
/// .
/// </summary>
public interface IProductOfferRepository
{
    Task<ProductOffer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default);

    /// <summary>Offres achetables d'un produit — c'est la Buy Box.</summary>
    Task<IReadOnlyList<ProductOffer>> ListActiveByProductAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les offres non archivées d'une boutique, pour AFFICHAGE, dans la limite de
    /// <paramref name="take"/> .
    /// </summary>
    Task<IReadOnlyList<ProductOffer>> ListByStoreAsync(
        Guid storeId, int take = 200, CancellationToken cancellationToken = default);

    /// <summary>Toutes les offres portant sur une variante donnée.</summary>
    Task<IReadOnlyList<ProductOffer>> ListByVariantAsync(
        Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Une boutique ne propose qu'une offre par variante.</summary>
    Task<bool> ExistsForStoreAndVariantAsync(
        Guid storeId, Guid variantId, CancellationToken cancellationToken = default);

    Task AddAsync(ProductOffer offer, CancellationToken cancellationToken = default);

    /// <summary>TOUTES les offres d'un vendeur, sans pagination et SUIVIES par EF.</summary>
    Task<IReadOnlyList<ProductOffer>> ListAllBySellerForUpdateAsync(
        Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>TOUTES les offres d'une BOUTIQUE, sans pagination et SUIVIES par EF.</summary>
    Task<IReadOnlyList<ProductOffer>> ListAllByStoreForUpdateAsync(
        Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Plusieurs offres par identifiant, en une requête.</summary>
    Task<IReadOnlyList<ProductOffer>> ListByIdsAsync(
        IReadOnlyCollection<OfferId> ids, CancellationToken cancellationToken = default);

    /// <summary>Les offres qui vendent une référence d'inventaire donnée.</summary>
    Task<IReadOnlyList<ProductOffer>> ListBySkuAsync(
        string sku, CancellationToken cancellationToken = default);
}
