using HBA.Gateway.Application.Contracts.Inventory;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>inventory-service</c> — stock et lieux d'expédition.</summary>
public interface IInventoryClient : IServiceClient
{
    /// <summary><c>GET /api/inventory/availability/{sku}</c>.</summary>
    Task<ServiceResult<StockAvailability>> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken);
}
