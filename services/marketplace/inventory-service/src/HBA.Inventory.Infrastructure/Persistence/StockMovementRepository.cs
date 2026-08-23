using HBA.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace HBA.Inventory.Infrastructure.Persistence;

internal sealed class StockMovementRepository : IStockMovementRepository
{
    private readonly InventoryDbContext _dbContext;

    public StockMovementRepository(InventoryDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(StockMovement movement, CancellationToken cancellationToken = default)
        => await _dbContext.Set<StockMovement>().AddAsync(movement, cancellationToken);

    /// <summary>
    /// `AsNoTracking` ET UNE BORNE : c'est une lecture d'écran. Suivre ces
    /// entités ferait payer un `DetectChanges` sur des centaines de lignes qu'on
    /// ne modifie jamais, dans la même unité de travail qu'une mutation de stock.
    /// </summary>
    public async Task<IReadOnlyList<StockMovement>> ListByItemAsync(
        Guid inventoryItemId, int take, CancellationToken cancellationToken = default)
        => await _dbContext.Set<StockMovement>()
            .AsNoTracking()
            .Where(m => m.InventoryItemId == inventoryItemId)
            .OrderByDescending(m => m.OccurredOnUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}
