using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Inventory;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Inventory;

/// <inheritdoc cref="IInventoryClient" />
public sealed class InventoryClient : ServiceHttpClient, IInventoryClient
{
    public InventoryClient(HttpClient http, ILogger<InventoryClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Inventory;

    public Task<ServiceResult<StockAvailability>> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken)
        => GetAsync<StockAvailability>(
            $"/api/inventory/availability/{Uri.EscapeDataString(sku)}", cancellationToken);
}
