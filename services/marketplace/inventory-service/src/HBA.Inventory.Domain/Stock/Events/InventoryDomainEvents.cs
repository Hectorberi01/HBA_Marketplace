using HBA.Shared.Domain.Events;

namespace HBA.Inventory.Domain.Stock.Events;

/// <summary>Un article de stock a été créé pour un SKU sur une localisation.</summary>
public sealed record InventoryItemCreatedDomainEvent(Guid InventoryItemId, string Sku, Guid LocationId) : DomainEvent;

/// <summary>Du stock a été réservé pour une commande.</summary>
public sealed record StockReservedDomainEvent(Guid InventoryItemId, string Sku, Guid OrderId, int Quantity) : DomainEvent;

/// <summary>Le stock disponible d'un SKU est tombé à zéro (rupture).</summary>
public sealed record StockDepletedDomainEvent(Guid InventoryItemId, string Sku, Guid LocationId) : DomainEvent;

/// <summary>
/// Le stock d'une référence repasse au-dessus de zéro.
///
/// CE PENDANT DE « StockDepleted » MANQUAIT, ET SON ABSENCE RENDAIT LA RUPTURE
/// DÉFINITIVE.
///
/// Sans lui, une offre passée en rupture par le stock ne pouvait être relancée
/// que par son vendeur, à la main — après avoir remarqué que ses ventes s'étaient
/// arrêtées. Un réassort livré le lundi ne remettait rien en vente.
/// </summary>
public sealed record StockReplenishedDomainEvent(Guid InventoryItemId, string Sku, Guid LocationId) : DomainEvent;
