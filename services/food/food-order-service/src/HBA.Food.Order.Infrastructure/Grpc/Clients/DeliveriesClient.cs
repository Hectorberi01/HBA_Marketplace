using Contracts = HBA.Deliveries.Contracts;

using HBA.Deliveries.Contracts;
using HBA.Deliveries.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProtoCreate = HBA.Deliveries.Grpc.V1.CreateDeliveryRequest;

using System.Globalization;
using System.Runtime.CompilerServices;

using ContratsDeliveries = HBA.Deliveries.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Deliveries.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.FoodOrders.Infrastructure.Grpc.Clients;

/// <summary>
/// Le moteur logistique vu depuis un donneur d'ordre — order-service ou
/// food-service.
/// </summary>
internal sealed class DeliveryGrpcClient : ContratsDeliveries.IDeliveryModuleApi, ContratsDeliveries.IDeliveryDispatchApi
{
    private readonly DeliveryApi.DeliveryApiClient _client;

    public DeliveryGrpcClient(DeliveryApi.DeliveryApiClient client) => _client = client;

    // ── Lecture ────────────────────────────────────────────────────────────

    public async Task<ContratsDeliveries.DeliverySummary?> GetAsync(
        Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetDeliveryAsync(
            new GetDeliveryRequest { DeliveryId = deliveryId.ToString() },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    public async Task<ContratsDeliveries.DeliverySummary?> GetByReferenceAsync(
        string reference, string source, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetDeliveryByReferenceAsync(
            new GetByReferenceRequest { Reference = reference, Source = source },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    public async Task<ContratsDeliveries.DeliveryTracking?> GetTrackingAsync(
        Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetTrackingAsync(
            new GetTrackingRequest { DeliveryId = deliveryId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return null;
        }

        return new ContratsDeliveries.DeliveryTracking(
            deliveryId,
            response.Status,
            response.HasDriverLatitude ? response.DriverLatitude : null,
            response.HasDriverLongitude ? response.DriverLongitude : null,
            Horodatage(response.HasPositionReportedAt ? response.PositionReportedAt : null),
            Vide(response.DriverName),
            Vide(response.DriverPhone));
    }

    public async Task<ContratsDeliveries.DriverAccount?> GetDriverAccountAsync(
        Guid driverId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ResolveDriverAsync(
            new ResolveDriverRequest { DriverId = driverId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found
            ? new ContratsDeliveries.DriverAccount(
                ToGuid(response.DriverId), ToGuid(response.UserId), response.FullName)
            : null;
    }

    // ── Écriture ───────────────────────────────────────────────────────────

    // `RequestQuoteAsync` ET `LookupQuoteAsync` ONT ÉTÉ RETIRÉS D'ICI.

    public async Task<ContratsDeliveries.DeliveryCreationResult> CreateAsync(
        ContratsDeliveries.CreateDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        var message = new ProtoCreate
        {
            Reference = request.Reference,
            Source = request.Source,
            Type = request.Type,
            IsCashOnDelivery = request.IsCashOnDelivery,
            Pickup = ToProto(request.Pickup),
            Dropoff = ToProto(request.Dropoff),
            Package = new DeliveryPackage
            {
                Description = request.Package.Description ?? string.Empty,
                WeightKg = request.Package.WeightKg is { } kg ? Montant(kg) : string.Empty,
                IsFragile = request.Package.IsFragile,
                IsPerishable = request.Package.IsPerishable
            }
        };

        // ON N'ENVOIE LA VALEUR DÉCLARÉE QUE SI ON EN A UNE.
        if (request.DeclaredValue is { } valeur)
        {
            message.DeclaredValue = Montant(valeur);
        }

        if (request.PartnerId is { } partner)
        {
            message.PartnerId = partner.ToString();
        }

        if (!string.IsNullOrWhiteSpace(request.QuoteId))
        {
            message.QuoteId = request.QuoteId;
        }

        if (request.ScheduledForUtc is { } quand)
        {
            message.ScheduledFor = quand.ToString("O", CultureInfo.InvariantCulture);
        }

        var response = await _client.CreateDeliveryAsync(message, cancellationToken: cancellationToken);

        return new ContratsDeliveries.DeliveryCreationResult(
            response.Succeeded,
            ToGuid(response.DeliveryId),
            Vide(response.Reason),
            Vide(response.ReasonCode));
    }

    /// <summary>Annule la course posée sous cette référence.</summary>
    public async Task<ContratsDeliveries.DeliveryCancellationResult> CancelByReferenceAsync(
        string reference, string source, string? reason, CancellationToken cancellationToken = default)
    {
        var message = new CancelDeliveryRequest { Reference = reference, Source = source };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            message.Reason = reason;
        }

        var response = await _client.CancelDeliveryAsync(message, cancellationToken: cancellationToken);

        return new ContratsDeliveries.DeliveryCancellationResult(
            response.Found, response.Cancelled, Vide(response.Reason), Vide(response.ReasonCode));
    }

    // ── Conversions ────────────────────────────────────────────────────────

    private static DeliveryStop ToProto(ContratsDeliveries.DeliveryStopRequest stop)
    {
        var message = new DeliveryStop
        {
            ContactName = stop.ContactName ?? string.Empty,
            Phone = stop.Phone ?? string.Empty,
            Commune = stop.Commune ?? string.Empty,
            Quartier = stop.Quartier ?? string.Empty,
            Landmark = stop.Landmark ?? string.Empty,
            Instructions = stop.Instructions ?? string.Empty
        };

        if (stop.Latitude is { } lat)
        {
            message.Latitude = lat;
        }

        if (stop.Longitude is { } lon)
        {
            message.Longitude = lon;
        }

        return message;
    }

    private static ContratsDeliveries.DeliverySummary? FromProto(GetDeliveryResponse response)
    {
        if (!response.Found || response.Delivery is null)
        {
            return null;
        }

        var d = response.Delivery;

        return new ContratsDeliveries.DeliverySummary(
            ToGuid(d.DeliveryId),
            d.Reference,
            d.Source,
            d.Type,
            d.Status,
            d.PickupSummary,
            d.DropoffSummary,
            Vide(d.DriverName),
            Vide(d.DriverPhone),
            Horodatage(d.CreatedAt) ?? default,
            Horodatage(d.HasAcceptedAt ? d.AcceptedAt : null),
            Horodatage(d.HasPickedUpAt ? d.PickedUpAt : null),
            Horodatage(d.HasDeliveredAt ? d.DeliveredAt : null));
    }

    // CHAÎNE VIDE ET NULL SE CONFONDENT EN PROTOBUF3.
    private static string? Vide(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static Guid ToGuid(string value) => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;

    private static string Montant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    // LE LECTEUR DE MONTANTS A ÉTÉ RETIRÉ AVEC LES DEUX MÉTHODES DE DEVIS.

    private static DateTime? Horodatage(string? value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}

internal static class DeliveryGrpcRegistration
{
    /// <summary>Enregistre LES DEUX interfaces sur la même implémentation.</summary>
    public static IServiceCollection AddDeliveryGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Delivery"]
            ?? throw new InvalidOperationException("Services:Delivery est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<DeliveryApi.DeliveryApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<DeliveryGrpcClient>();
        services.AddScoped<ContratsDeliveries.IDeliveryModuleApi>(sp => sp.GetRequiredService<DeliveryGrpcClient>());
        services.AddScoped<ContratsDeliveries.IDeliveryDispatchApi>(sp => sp.GetRequiredService<DeliveryGrpcClient>());

        return services;
    }
}
