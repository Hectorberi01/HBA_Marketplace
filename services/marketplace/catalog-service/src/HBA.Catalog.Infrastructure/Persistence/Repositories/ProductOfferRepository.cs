using Microsoft.EntityFrameworkCore;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Infrastructure.Persistence;

/// <summary>Accès aux offres.</summary>
internal sealed class ProductOfferRepository : IProductOfferRepository
{
    private readonly CatalogDbContext _dbContext;

    public ProductOfferRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task<ProductOffer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListActiveByProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
        // Triées par prix acheteur croissant : c'est l'ordre de la Buy Box, et le
        // laisser au consommateur reviendrait à ce que la vitrine et l'application
        // le trient différemment.
        => await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.ProductId == productId && o.Status == OfferStatus.Active)
            .OrderBy(o => o.BuyerPrice.Amount)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListByStoreAsync(
        Guid storeId, int take = 200, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.StoreId == storeId && o.Status != OfferStatus.Archived)
            .OrderByDescending(o => o.CreatedOnUtc)
            .Take(take <= 0 ? 200 : take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListAllBySellerForUpdateAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .Where(o => o.SellerId == sellerId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListAllByStoreForUpdateAsync(
        Guid storeId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .Where(o => o.StoreId == storeId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListByVariantAsync(
        Guid variantId, CancellationToken cancellationToken = default)
        // SUIVIES (pas de `AsNoTracking`) : l'appelant archive ces offres quand la
        // variante est désactivée.
        => await _dbContext.Offers
            .Where(o => o.VariantId == variantId && o.Status != OfferStatus.Archived)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsForStoreAndVariantAsync(
        Guid storeId, Guid variantId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .AsNoTracking()
            .AnyAsync(
                o => o.StoreId == storeId && o.VariantId == variantId && o.Status != OfferStatus.Archived,
                cancellationToken);

    public async Task AddAsync(ProductOffer offer, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.AddAsync(offer, cancellationToken);
    public async Task<IReadOnlyList<ProductOffer>> ListByIdsAsync(
        IReadOnlyCollection<OfferId> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            // Sans ce court-circuit, EF traduirait `IN ()`, que PostgreSQL refuse.
            return [];
        }

        return await _dbContext.Offers
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductOffer>> ListBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var reference = Sku.Create(sku);
        if (reference.IsFailure)
        {
            // Une référence illisible ne désigne aucune variante : liste vide, pas
            // d'exception.
            return [];
        }

        var valeur = reference.Value;

        // LA VARIANTE PORTE LE SKU, PAS L'OFFRE — d'où la jointure.
        var variantIds = await _dbContext.Products
            .AsNoTracking()
            .SelectMany(p => p.Variants)
            .Where(v => v.Sku == valeur)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        if (variantIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Offers
            .AsNoTracking()
            .Where(o => variantIds.Contains(o.VariantId) && o.Status != OfferStatus.Archived)
            .ToListAsync(cancellationToken);
    }
}
