using System.Runtime.CompilerServices;
using System.Globalization;
using HBA.Deliveries.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Contracts = HBA.Deliveries.Contracts;

// L'ESPACE ENGLOBANT L'EMPORTE SUR LES `using`, ET C'EST UN PIÈGE SILENCIEUX.
//
// Ce fichier déclare `namespace HBA.Deliveries.Contracts.Grpc`. Pour le
// compilateur, `HBA.Deliveries.Contracts` est donc un espace ENGLOBANT, et ses
// types priment sur ceux importés par `using HBA.Deliveries.Grpc.V1`.
//
// Deux noms existent des deux côtés — `CreateDeliveryRequest` et
// `DeliverySummary`. Écrire `new CreateDeliveryRequest { … }` construisait donc
// l'enregistrement des CONTRATS au lieu du message PROTO, et le compilateur ne
// s'en plaignait qu'aux sept lignes suivantes, sur des conversions impossibles.
// L'erreur ne désignait jamais sa cause.
//
// L'alias rend le choix explicite plutôt que de le laisser aux règles de
// résolution.
using ProtoCreate = HBA.Deliveries.Grpc.V1.CreateDeliveryRequest;

namespace HBA.Deliveries.Contracts.Grpc;

/// <summary>
/// Le moteur logistique vu depuis un donneur d'ordre — order-service ou
/// food-service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE PROJET NE CONTIENT QUE LE CLIENT. LE SERVEUR VIT DANS delivery-service.
///
/// Les autres contrats gRPC de la plateforme hébergent les deux côtés ici, parce
/// qu'ils ne font que LIRE : un `IXxxModuleApi` suffit, et il est déclaré dans
/// un projet `*.Contracts` sans dépendance.
///
/// Celui-ci ÉCRIT. Servir `CreateDelivery` demande d'envoyer un
/// `CreateDeliveryCommand` par MediatR, donc de référencer la couche Application
/// de delivery-service. Placer cela dans `shared` ferait dépendre le socle
/// partagé de l'intérieur d'un service — et n'importe quel autre service tirerait
/// cette dépendance en référençant le client.
///
/// Le serveur est donc dans `HBA.Deliveries.Api/Grpc/`, où l'Application est
/// légitimement accessible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class DeliveryGrpcClient : Contracts.IDeliveryModuleApi, Contracts.IDeliveryDispatchApi
{
    private readonly DeliveryApi.DeliveryApiClient _client;

    public DeliveryGrpcClient(DeliveryApi.DeliveryApiClient client) => _client = client;

    // ── Lecture ────────────────────────────────────────────────────────────

    public async Task<Contracts.DeliverySummary?> GetAsync(
        Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetDeliveryAsync(
            new GetDeliveryRequest { DeliveryId = deliveryId.ToString() },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    public async Task<Contracts.DeliverySummary?> GetByReferenceAsync(
        string reference, string source, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetDeliveryByReferenceAsync(
            new GetByReferenceRequest { Reference = reference, Source = source },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    public async Task<Contracts.DeliveryTracking?> GetTrackingAsync(
        Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetTrackingAsync(
            new GetTrackingRequest { DeliveryId = deliveryId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return null;
        }

        return new Contracts.DeliveryTracking(
            deliveryId,
            response.Status,
            response.HasDriverLatitude ? response.DriverLatitude : null,
            response.HasDriverLongitude ? response.DriverLongitude : null,
            Horodatage(response.HasPositionReportedAt ? response.PositionReportedAt : null),
            Vide(response.DriverName),
            Vide(response.DriverPhone));
    }

    public async Task<Contracts.DriverAccount?> GetDriverAccountAsync(
        Guid driverId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ResolveDriverAsync(
            new ResolveDriverRequest { DriverId = driverId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found
            ? new Contracts.DriverAccount(
                ToGuid(response.DriverId), ToGuid(response.UserId), response.FullName)
            : null;
    }

    // ── Écriture ───────────────────────────────────────────────────────────

    // ═════════════════════════════════════════════════════════════════════════
    // `RequestQuoteAsync` ET `LookupQuoteAsync` ONT ÉTÉ RETIRÉS D'ICI.
    //
    // Ils enveloppaient `DeliveryApi.GetQuote` et `DeliveryApi.LookupQuote`, deux
    // RPC SANS CORPS DE SERVEUR. `LookupQuoteAsync` était le seul des deux à être
    // appelé — par les deux checkouts — et il rendait `UNIMPLEMENTED`. Le devis
    // étant obligatoire pour un repas, aucune commande de repas n'aboutissait.
    //
    // Le devis se demande et se relit maintenant chez delivery-pricing :
    // `DeliveryQuoteLookupClient`, dans `HBA.DeliveryPricing.Contracts.Grpc`.
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<Contracts.DeliveryCreationResult> CreateAsync(
        Contracts.CreateDeliveryRequest request, CancellationToken cancellationToken = default)
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
        //
        // `declared_value` est `optional` : ne pas poser le champ et poser une
        // chaîne vide sont DEUX choses différentes de l'autre côté. Le serveur
        // teste `HasDeclaredValue`, et une chaîne vide passerait ce test pour
        // échouer ensuite à l'analyse du montant.
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

        return new Contracts.DeliveryCreationResult(
            response.Succeeded,
            ToGuid(response.DeliveryId),
            Vide(response.Reason),
            Vide(response.ReasonCode));
    }

    /// <summary>
    /// Annule la course posée sous cette référence.
    /// </summary>
    /// <remarks>
    /// AUCUNE EXCEPTION SUR « INTROUVABLE ».
    ///
    /// La plupart des commandes sont annulées avant confirmation, donc avant
    /// qu'une course n'existe. Le serveur rend `found = false`, et l'appelant
    /// n'a rien à faire — c'est le cas normal, pas un incident.
    /// </remarks>
    public async Task<Contracts.DeliveryCancellationResult> CancelByReferenceAsync(
        string reference, string source, string? reason, CancellationToken cancellationToken = default)
    {
        var message = new CancelDeliveryRequest { Reference = reference, Source = source };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            message.Reason = reason;
        }

        var response = await _client.CancelDeliveryAsync(message, cancellationToken: cancellationToken);

        return new Contracts.DeliveryCancellationResult(
            response.Found, response.Cancelled, Vide(response.Reason), Vide(response.ReasonCode));
    }

    // ── Conversions ────────────────────────────────────────────────────────

    private static DeliveryStop ToProto(Contracts.DeliveryStopRequest stop)
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

    private static Contracts.DeliverySummary? FromProto(GetDeliveryResponse response)
    {
        if (!response.Found || response.Delivery is null)
        {
            return null;
        }

        var d = response.Delivery;

        return new Contracts.DeliverySummary(
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
    //
    // Un champ absent arrive comme "". Rendre "" plutôt que null ferait afficher
    // un nom de livreur vide là où l'interface doit dire « pas encore attribué ».
    private static string? Vide(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static Guid ToGuid(string value) => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;

    private static string Montant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    // LE LECTEUR DE MONTANTS A ÉTÉ RETIRÉ AVEC LES DEUX MÉTHODES DE DEVIS.
    //
    // Il ne servait qu'à `RequestQuoteAsync` et `LookupQuoteAsync`, partis chez
    // delivery-pricing. Les seuls montants qui traversent encore ce client — poids
    // du colis, valeur déclarée — sont ÉCRITS, jamais lus. Le laisser aurait été
    // du code mort avec l'air d'une règle.

    private static DateTime? Horodatage(string? value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}

public static class DeliveryGrpcRegistration
{
    /// <summary>
    /// Enregistre LES DEUX interfaces sur la même implémentation.
    /// </summary>
    /// <remarks>
    /// La séparation lecture/écriture est une frontière de CONCEPTION, pas de
    /// transport : elle dit qui a le droit de quoi, pas par où ça passe. Un
    /// appelant qui n'injecte que <c>IDeliveryModuleApi</c> ne peut pas créer de
    /// course, même si l'objet derrière en est capable.
    /// </remarks>
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
        services.AddScoped<Contracts.IDeliveryModuleApi>(sp => sp.GetRequiredService<DeliveryGrpcClient>());
        services.AddScoped<Contracts.IDeliveryDispatchApi>(sp => sp.GetRequiredService<DeliveryGrpcClient>());

        return services;
    }
}
