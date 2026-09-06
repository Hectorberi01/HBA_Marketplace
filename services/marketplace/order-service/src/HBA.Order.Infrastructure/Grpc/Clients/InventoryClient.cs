using Contracts = HBA.Inventory.Contracts;

using Grpc.Core;
using HBA.Inventory.Contracts;
using HBA.Inventory.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Inventory.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 5 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Orders.Infrastructure.Grpc.Clients;

internal sealed class InventoryGrpcClient : Contracts.IInventoryModuleApi
{
    private readonly InventoryApi.InventoryApiClient _client;

    public InventoryGrpcClient(InventoryApi.InventoryApiClient client) => _client = client;

    public async Task<Contracts.AvailabilitySummary> GetAvailabilityAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetAvailabilityAsync(
            new GetAvailabilityRequest { Sku = sku },
            cancellationToken: cancellationToken);

        return new Contracts.AvailabilitySummary(response.Sku, response.Available);
    }

    /// <summary>
    /// CETTE MÉTHODE RENDAIT `null` EN DUR.
    ///
    /// `Task.FromResult(null)` — un bouchon qui compile, ne lève pas, et ment :
    /// tout appelant concluait « ce lieu n'existe pas » alors qu'il n'avait
    /// jamais été demandé. Or c'est ce lieu qui porte l'adresse d'ENLÈVEMENT
    /// d'une course. Aucun colis, aucun repas ne pouvait donc être confié à un
    /// livreur, et rien ne le signalait.
    ///
    /// Un bouchon silencieux est pire qu'une exception `NotImplementedException` :
    /// celle-ci se voit au premier appel.
    /// </summary>
    public async Task<Contracts.FulfillmentLocationSummary?> GetLocationAsync(
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

        return new Contracts.FulfillmentLocationSummary(
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

    // Chaîne vide et null se confondent en protobuf3 : un champ absent arrive
    // comme "". Un quartier vide n'est pas un quartier nommé « ».
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

        services.AddScoped<Contracts.IInventoryModuleApi, InventoryGrpcClient>();

        return services;
    }
}
