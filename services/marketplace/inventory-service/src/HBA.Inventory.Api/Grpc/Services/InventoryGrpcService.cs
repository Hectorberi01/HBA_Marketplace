using Contracts = HBA.Inventory.Contracts;

using Grpc.Core;
using HBA.Inventory.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


using HBA.Inventory.Contracts;
using ContratsInventory = HBA.Inventory.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Inventory.Contracts.Grpc` (lot B de la migration gRPC).
//
// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES
// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient
// l'implementation de seller-service ; les huit qui consomment order.proto
// liaient celle d'order-service. Aucun ne s'en servait.
//
// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.
// L'assemblage de contrats ne porte plus que le stub genere, le client et son
// enregistrement — le lot C descendra ces deux-la chez les appelants.
//
// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours
// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes
// intercepteurs. Un deplacement de fichier ne rend rien plus sur.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Inventory.Api.Grpc.Services;

internal sealed class InventoryGrpcService : InventoryApi.InventoryApiBase
{
    private readonly ContratsInventory.IInventoryModuleApi _inventory;

    public InventoryGrpcService(ContratsInventory.IInventoryModuleApi inventory) => _inventory = inventory;

    public override async Task<GetAvailabilityResponse> GetAvailability(
        GetAvailabilityRequest request, ServerCallContext context)
    {
        var availability = await _inventory.GetAvailabilityAsync(request.Sku, context.CancellationToken);
        return new GetAvailabilityResponse
        {
            Sku = availability.Sku,
            Available = availability.TotalAvailable,
            InStock = availability.TotalAvailable > 0
        };
    }

    public override async Task<StockOperationResponse> ReserveStock(
        ReserveStockRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.LocationId, out var locationId)
            || !Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "location_id/order_id invalide."));
        }

        var succeeded = await _inventory.TryReserveAsync(
            request.Sku, locationId, orderId, request.Quantity, context.CancellationToken);

        return new StockOperationResponse { Succeeded = succeeded };
    }

    public override async Task<StockOperationResponse> ReleaseReservation(
        ReservationKeyRequest request, ServerCallContext context)
    {
        var (locationId, orderId) = ParseReservationKey(request);
        await _inventory.ReleaseReservationAsync(request.Sku, locationId, orderId, context.CancellationToken);
        return new StockOperationResponse { Succeeded = true };
    }

    public override async Task<StockOperationResponse> ConfirmReservation(
        ReservationKeyRequest request, ServerCallContext context)
    {
        var (locationId, orderId) = ParseReservationKey(request);
        var succeeded = await _inventory.ConfirmReservationAsync(
            request.Sku, locationId, orderId, context.CancellationToken);
        return new StockOperationResponse { Succeeded = succeeded };
    }

    public override async Task<GetLocationResponse> GetLocation(
        GetLocationRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.LocationId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "location_id n'est pas un GUID."));
        }

        var lieu = await _inventory.GetLocationAsync(id, context.CancellationToken);

        if (lieu is null)
        {
            return new GetLocationResponse { Found = false };
        }

        var message = new FulfillmentLocation
        {
            LocationId = lieu.Id.ToString(),
            Type = lieu.Type,
            OwnerId = lieu.OwnerId?.ToString() ?? string.Empty,
            CommuneCode = lieu.CommuneCode,
            CommuneName = lieu.CommuneName,
            Quartier = lieu.Quartier ?? string.Empty,
            Landmark = lieu.Landmark ?? string.Empty,
            Line = lieu.Line ?? string.Empty,
            CountryCode = lieu.CountryCode,
            ContactPhone = lieu.ContactPhone ?? string.Empty
        };

        // RECOPIÉES, ET C'EST UN RAPPEL PLUTÔT QU'UN MAPPING MUET.
        //
        // Une projection jumelle les écrasait autrefois par « null, null » : une
        // saisie GPS ne survivait pas à sa propre relecture. Écrire la même
        // projection à un second endroit est l'occasion parfaite de refaire
        // l'erreur.
        if (lieu.Latitude is { } lat)
        {
            message.Latitude = lat;
        }

        if (lieu.Longitude is { } lon)
        {
            message.Longitude = lon;
        }

        return new GetLocationResponse { Found = true, Location = message };
    }

    private static (Guid LocationId, Guid OrderId) ParseReservationKey(ReservationKeyRequest request)
    {
        if (!Guid.TryParse(request.LocationId, out var locationId)
            || !Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "location_id/order_id invalide."));
        }

        return (locationId, orderId);
    }
}
