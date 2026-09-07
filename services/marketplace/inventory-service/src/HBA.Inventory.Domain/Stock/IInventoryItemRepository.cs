namespace HBA.Inventory.Domain.Stock;

public interface IInventoryItemRepository
{
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetByIdAsync(InventoryItemId id, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetBySkuAndLocationAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryItem>> ListBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>Articles de stock situés dans un ensemble de localisations.</summary>
    Task<IReadOnlyList<InventoryItem>> ListByLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les articles sous leur seuil de réapprovisionnement, dans la limite de
    /// <paramref name="take"/> .
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> ListLowStockAsync(
        int take = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Articles portant au moins une réservation `Active` dont l'échéance est
    /// dépassée.
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> ListWithExpirableReservationsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string sku, Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Efface les réservations TERMINÉES antérieures à <paramref name="avantUtc"/>
    /// .
    /// </summary>
    /// <param name="avantUtc">Borne haute : les lignes terminées AVANT cet instant.</param>
    /// <param name="plafond">Nombre maximum de lignes effacées en un tour.</param>
    /// <returns>Le nombre de lignes réellement effacées.</returns>
    Task<int> PurgeTerminalReservationsAsync(
        DateTime avantUtc, int plafond, CancellationToken cancellationToken = default);
}
