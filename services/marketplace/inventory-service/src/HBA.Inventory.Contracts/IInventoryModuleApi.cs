namespace HBA.Inventory.Contracts;

/// <summary>API in-process publique du module Inventory.</summary>
public interface IInventoryModuleApi
{
    Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>Un lieu d'expédition, par son identifiant.</summary>
    Task<FulfillmentLocationSummary?> GetLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Réserve du stock pour une commande (étape du Saga Ordering).</summary>
    Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Libère la réservation d'une commande (compensation : paiement échoué /
    /// annulation).
    /// </summary>
    Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Confirme la vente : décrémente le stock physique et solde la réservation.</summary>
    Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default);
}
