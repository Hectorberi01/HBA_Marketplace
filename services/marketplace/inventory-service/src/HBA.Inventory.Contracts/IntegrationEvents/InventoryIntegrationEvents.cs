using HBA.Shared.IntegrationEvents;

namespace HBA.Inventory.Contracts.IntegrationEvents;

/// <summary>Du stock a été réservé pour une commande (consommé par Ordering).</summary>
public sealed record StockReservedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid OrderId { get; init; }
    public required int Quantity { get; init; }
}

/// <summary>Un SKU est en rupture (consommé par Offers pour passer l'offre OutOfStock, Search…).</summary>
public sealed record StockDepletedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid LocationId { get; init; }
}

/// <summary>
/// Le stock d'un SKU repasse au-dessus de zéro.
///
/// Consommé par le composition root pour relancer les offres que la rupture avait
/// retirées de la vente. Sans lui, un réassort ne remet rien en vente : le
/// vendeur doit s'en apercevoir et relancer chaque offre à la main.
/// </summary>
public sealed record StockReplenishedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid LocationId { get; init; }
}
