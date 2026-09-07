using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Application;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;
using HBA.Merchants.Infrastructure.Persistence;

namespace HBA.Merchants.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Sellers.</summary>
internal sealed class SellerModuleApi : ISellerModuleApi
{
    private readonly SellersDbContext _dbContext;
    private readonly ICacheService _cache;
    private readonly IPlatformPricing _pricing;

    public SellerModuleApi(SellersDbContext dbContext, ICacheService cache, IPlatformPricing pricing)
    {
        _dbContext = dbContext;
        _cache = cache;
        _pricing = pricing;
    }

    /// <summary>PAS DE CACHE ICI, CONTRAIREMENT AUX LECTURES DE VENDEUR.</summary>
    public async Task<StoreSummary?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
    {
        var id = new StoreId(storeId);
        var store = await _dbContext.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return store is null ? null : MapStore(store);
    }

    public async Task<IReadOnlyList<StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var stores = await _dbContext.Stores
            .AsNoTracking()
            .Where(s => s.SellerId == sellerId)
            .OrderBy(s => s.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return stores.Select(MapStore).ToList();
    }

    private static StoreSummary MapStore(Store store)
        => new(
            store.Id.Value,
            store.SellerId,
            store.Name,
            store.LogoUrl,
            store.Description,
            store.Contact.Phone,
            store.Contact.Email,
            store.Status.ToString(),
            store.IsSelling,
            store.FulfillmentLocationId,
            store.StatusReason,
            store.OpeningHours
                // Lundi en tête : DayOfWeek vaut Sunday = 0.
                .OrderBy(h => ((int)h.Day + 6) % 7)
                .ThenBy(h => h.OpensAt)
                .Select(h => new StoreOpeningHourSummary(
                    h.Day.ToString(),
                    h.OpensAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                    h.ClosesAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList(),
            store.CreatedOnUtc);

    public Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.Seller(sellerId),
            async ct =>
            {
                var id = new SellerId(sellerId);

                // PLUS DE `.Include(KybDocuments)`, ET C'EST UN GAIN, PAS UN OUBLI.
                var seller = await _dbContext.Sellers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == id, ct);

                return seller is null ? null : Map(seller);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

    public Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.SellerByUser(userId),
            async ct =>
            {
                var seller = await _dbContext.Sellers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == userId, ct);

                return seller is null ? null : Map(seller);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

    /// <summary>Vendeur actif ?</summary>
    public async Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await GetSellerAsync(sellerId, cancellationToken);
        return seller is not null
            && string.Equals(seller.Status, nameof(SellerStatus.Active), StringComparison.Ordinal);
    }

    /// <summary>LE COMPTE DE REVERSEMENT — LU DIRECTEMENT, SANS CACHE ET SANS LES PIÈCES.</summary>
    public async Task<SellerPayout> GetSellerPayoutAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var id = new SellerId(sellerId);

        var seller = await _dbContext.Sellers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (seller is null)
        {
            return SellerPayout.Unknown;
        }

        return seller.PayoutAccount is { } compte
            ? SellerPayout.Of(new PayoutAccountSummary(
                compte.Provider.ToString(), compte.AccountNumber, compte.AccountName))
            : SellerPayout.NotConfigured;
    }

    /// <summary>LES HUIT CHAMPS QUI VOYAGENT, ET RIEN D'AUTRE.</summary>
    private SellerSummary Map(Seller seller) => new(
        seller.Id.Value,
        seller.UserId,
        seller.ShopName,
        seller.LogoUrl,
        seller.Description,
        seller.Status.ToString(),
        seller.KybStatus.ToString(),
        _pricing.CommissionRate);   // le taux APPLIQUÉ, pas la colonne morte

}
