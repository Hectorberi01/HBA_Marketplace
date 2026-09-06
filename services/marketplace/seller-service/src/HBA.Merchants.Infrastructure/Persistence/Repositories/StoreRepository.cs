using Microsoft.EntityFrameworkCore;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Infrastructure.Persistence;

internal sealed class StoreRepository : IStoreRepository
{
    private readonly SellersDbContext _dbContext;

    public StoreRepository(SellersDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF : toutes les lectures de ce dépôt précèdent une mutation (ouvrir,
    // fermer, changer les horaires). Les écrans passent par les requêtes, qui
    // projettent sans suivre.
    public async Task<Store?> GetByIdAsync(StoreId id, CancellationToken cancellationToken = default)
        => await _dbContext.Stores.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Store>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Stores
            .Where(s => s.SellerId == sellerId)
            .OrderBy(s => s.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Store store, CancellationToken cancellationToken = default)
        => await _dbContext.Stores.AddAsync(store, cancellationToken);
}
