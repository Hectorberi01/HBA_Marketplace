using Contracts = HBA.Inventory.Contracts;

using Grpc.Core;
using HBA.Inventory.Contracts;
using HBA.Inventory.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


using ContratsInventory = HBA.Inventory.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Inventory.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Commerce.Infrastructure.Grpc.Clients;

internal sealed class InventoryGrpcClient : ContratsInventory.IInventoryModuleApi
{
    private readonly InventoryApi.InventoryApiClient _client;

    public InventoryGrpcClient(InventoryApi.InventoryApiClient client) => _client = client;

    public async Task<ContratsInventory.AvailabilitySummary> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAvailabilityAsync(
            new GetAvailabilityRequest { Sku = sku },
            cancellationToken: cancellationToken);

        return new ContratsInventory.AvailabilitySummary(response.Sku, response.Available);
    }

    /// <summary>CETTE MÉTHODE RENDAIT `null` EN DUR.</summary>
    public async Task<ContratsInventory.FulfillmentLocationSummary?> GetLocationAsync(
        Guid locationId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetLocationAsync(
            new GetLocationRequest { LocationId = locationId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found || response.Location is null)
        {
            return null;
        }

        var l = response.Location;

        return new ContratsInventory.FulfillmentLocationSummary(
            Guid.TryParse(l.LocationId, out var id) ? id : Guid.Empty,
            l.Type,
            Guid.TryParse(l.OwnerId, out var owner) ? owner : null,
            l.CommuneCode,
            l.CommuneName,
            Vide(l.Quartier),
            Vide(l.Landmark),
            Vide(l.Line),
            l.CountryCode,
            l.HasLatitude ? l.Latitude : null,
            l.HasLongitude ? l.Longitude : null,
            Vide(l.ContactPhone));
    }

    // Chaîne vide et null se confondent en protobuf3 : un champ absent arrive comme
    // "".
    private static string? Vide(string value) => string.IsNullOrEmpty(value) ? null : value;

    public async Task<bool> IsInStockAsync(
        string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var availability = await GetAvailabilityAsync(sku, cancellationToken);
        return availability.TotalAvailable >= quantity;
    }

    public async Task<bool> TryReserveAsync(
        string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
    {
        var response = await _client.ReserveStockAsync(
            new ReserveStockRequest
            {
                Sku = sku,
                LocationId = locationId.ToString(),
                OrderId = orderId.ToString(),
                Quantity = quantity,
                ExpiresInMinutes = 15
            },
            cancellationToken: cancellationToken);

        return response.Succeeded;
    }

    public async Task ReleaseReservationAsync(
        string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        await _client.ReleaseReservationAsync(
            new ReservationKeyRequest
            {
                Sku = sku,
                LocationId = locationId.ToString(),
                OrderId = orderId.ToString()
            },
            cancellationToken: cancellationToken);
    }

    public async Task<bool> ConfirmReservationAsync(
        string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ConfirmReservationAsync(
            new ReservationKeyRequest
            {
                Sku = sku,
                LocationId = locationId.ToString(),
                OrderId = orderId.ToString()
            },
            cancellationToken: cancellationToken);

        return response.Succeeded;
    }
}

internal static class InventoryGrpcRegistration
{
    public static IServiceCollection AddInventoryGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Inventory"]
            ?? throw new InvalidOperationException("Services:Inventory est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<InventoryApi.InventoryApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<ContratsInventory.IInventoryModuleApi, InventoryGrpcClient>();

        return services;
    }
}
