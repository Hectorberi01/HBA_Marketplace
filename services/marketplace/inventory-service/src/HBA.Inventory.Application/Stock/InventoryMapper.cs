using HBA.Inventory.Contracts;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock;

internal static class InventoryMapper
{
    public static InventoryItemSummary ToSummary(InventoryItem item) => new(
        item.Id.Value,
        item.Sku.Value,
        item.LocationId,
        item.OnHand,
        item.Reserved,
        item.Available,
        item.ReorderThreshold,
        item.IsLowStock);
}
