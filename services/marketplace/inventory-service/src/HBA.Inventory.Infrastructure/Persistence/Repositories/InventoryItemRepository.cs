using Microsoft.EntityFrameworkCore;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Infrastructure.Persistence;

internal sealed class InventoryItemRepository : IInventoryItemRepository
{
    /// <summary>Le plafond de l'alerte de réapprovisionnement.</summary>
    private const int PlafondDAlerte = 200;

    private readonly InventoryDbContext _dbContext;

    public InventoryItemRepository(InventoryDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default)
        => await _dbContext.InventoryItems.AddAsync(item, cancellationToken);

    public async Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default)
        => await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return null;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .FirstOrDefaultAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return Array.Empty<InventoryItem>();
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListByLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        // Un IN () vide est une requête inutile, et selon le fournisseur une
        // syntaxe invalide.
        if (locationIds.Count == 0)
        {
            return Array.Empty<InventoryItem>();
        }

        // `Include(Reservations)` est indispensable : `Available` et `IsLowStock`
        // sont calculés à partir des réservations.
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => locationIds.Contains(i.LocationId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Les articles sous leur seuil de réapprovisionnement, filtrés PAR LA BASE.</summary>
    public async Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(
        int take = PlafondDAlerte, CancellationToken cancellationToken = default)
    {
        var candidats = await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i =>
                i.OnHand
                - i.Reservations
                    .Where(r => r.Status == ReservationStatus.Active)
                    .Sum(r => r.Quantity)
                <= i.ReorderThreshold)
            .OrderBy(i => i.Id)
            .Take(take <= 0 ? PlafondDAlerte : take)
            .ToListAsync(cancellationToken);

        // Confirmation par le domaine — voir l'encadré : ce filtre ne peut que
        // retirer, jamais ajouter.
        return candidats.Where(i => i.IsLowStock).ToList();
    }

    public async Task<IReadOnlyList<InventoryItem>> ListWithExpirableReservationsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return Array.Empty<InventoryItem>();
        }

        // LE FILTRE EST TRADUIT EN SQL, L'`Include` RAMÈNE TOUT LE RESTE.
        return await _dbContext.InventoryItems
            .Include(i => i.Reservations)
            .Where(i => i.Reservations.Any(r =>
                r.Status == ReservationStatus.Active && r.ExpiresAtUtc <= nowUtc))
            .OrderBy(i => i.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Efface les réservations terminées antérieures à la borne.</summary>
    public async Task<int> PurgeTerminalReservationsAsync(
        DateTime avantUtc, int plafond, CancellationToken cancellationToken = default)
    {
        // « TERMINÉE » SE LIT SUR LE STATUT, PAS SUR L'ÉCHÉANCE.
        var termines = new[]
        {
            ReservationStatus.Confirmed,
            ReservationStatus.Released,
            ReservationStatus.Expired
        };

        var lot = await _dbContext.Set<StockReservation>()
            .Where(r => termines.Contains(r.Status)
                && (r.ConfirmedAtUtc < avantUtc
                    || r.ReleasedAtUtc < avantUtc
                    || r.ExpiredAtUtc < avantUtc))
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(plafond)
            .ToListAsync(cancellationToken);

        if (lot.Count == 0)
        {
            return 0;
        }

        return await _dbContext.Set<StockReservation>()
            .Where(r => lot.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems.AnyAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }
}
