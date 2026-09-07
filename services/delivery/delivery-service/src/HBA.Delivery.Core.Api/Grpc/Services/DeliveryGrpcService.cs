using Contracts = HBA.Deliveries.Contracts;
using Grpc.Core;
using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Domain.Deliveries;

using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using MediatR;

using ProtoStop = HBA.Deliveries.Grpc.V1.DeliveryStop;
using ProtoSummary = HBA.Deliveries.Grpc.V1.DeliverySummary;

using System.Globalization;
using System.Runtime.CompilerServices;

// DEPLACE DEPUIS `HBA.Deliveries.Api.Grpc` (lot B de la migration gRPC).

namespace HBA.Deliveries.Api.Grpc.Services;

/// <summary>Le moteur logistique, servi à ses donneurs d'ordre.</summary>
internal sealed class DeliveryGrpcService : DeliveryApi.DeliveryApiBase
{
    private readonly ISender _sender;
    private readonly Contracts.IDeliveryModuleApi _deliveries;

    public DeliveryGrpcService(ISender sender, Contracts.IDeliveryModuleApi deliveries)
    {
        _sender = sender;
        _deliveries = deliveries;
    }

    public override async Task<CreateDeliveryResponse> CreateDelivery(
        CreateDeliveryRequest request, ServerCallContext context)
    {
        var source = Enumeration<DeliverySource>(request.Source, nameof(request.Source));
        var type = Enumeration<DeliveryType>(request.Type, nameof(request.Type));

        // `required_proof` A ÉTÉ RETIRÉ DU CONTRAT — ISSUE-057.
        var valeurDeclaree = request.HasDeclaredValue && !string.IsNullOrWhiteSpace(request.DeclaredValue)
            ? Montant(request.DeclaredValue)
            : (decimal?)null;

        if (request.Pickup is null || request.Dropoff is null || request.Package is null)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument, "pickup, dropoff et package sont obligatoires."));
        }

        var commande = new CreateDeliveryCommand(
            request.Reference,
            source,
            type,
            Arret(request.Pickup),
            Arret(request.Dropoff),
            new DeliveryPackageInput(
                Vide(request.Package.Description),
                string.IsNullOrEmpty(request.Package.WeightKg) ? null : Montant(request.Package.WeightKg),
                request.Package.IsFragile,
                request.Package.IsPerishable),
            valeurDeclaree,
            request.IsCashOnDelivery,
            request.HasPartnerId && Guid.TryParse(request.PartnerId, out var partenaire) ? partenaire : null,
            request.HasQuoteId ? request.QuoteId : null,
            request.HasScheduledFor ? Horodatage(request.ScheduledFor) : null);

        var resultat = await _sender.Send(commande, context.CancellationToken);

        // UN REFUS MÉTIER VOYAGE DANS LA RÉPONSE, PAS DANS UNE EXCEPTION.
        return resultat.IsFailure
            ? new CreateDeliveryResponse
            {
                Succeeded = false,
                DeliveryId = string.Empty,
                ReasonCode = resultat.Error.Code,
                Reason = resultat.Error.Message
            }
            : new CreateDeliveryResponse
            {
                Succeeded = true,
                DeliveryId = resultat.Value.ToString(),
                Reason = string.Empty
            };
    }

    /// <summary>LE DONNEUR D'ORDRE ANNULE SA COURSE.</summary>
    public override async Task<CancelDeliveryResponse> CancelDelivery(
        CancelDeliveryRequest request, ServerCallContext context)
    {
        var course = await _deliveries.GetByReferenceAsync(
            request.Reference, request.Source, context.CancellationToken);

        if (course is null)
        {
            return new CancelDeliveryResponse
            {
                Found = false,
                Cancelled = false,
                Reason = string.Empty
            };
        }

        var resultat = await _sender.Send(
            new CancelDeliveryCommand(
                course.Id,
                request.HasReason ? request.Reason : null,
                RequiredPartnerId: null),
            context.CancellationToken);

        return resultat.IsFailure
            ? new CancelDeliveryResponse
            {
                Found = true,
                Cancelled = false,
                ReasonCode = resultat.Error.Code,
                Reason = resultat.Error.Message
            }
            : new CancelDeliveryResponse
            {
                Found = true,
                Cancelled = true,
                Reason = string.Empty
            };
    }

    public override async Task<GetDeliveryResponse> GetDelivery(
        GetDeliveryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id n'est pas un GUID."));
        }

        return Reponse(await _deliveries.GetAsync(id, context.CancellationToken));
    }

    public override async Task<GetDeliveryResponse> GetDeliveryByReference(
        GetByReferenceRequest request, ServerCallContext context)
        => Reponse(await _deliveries.GetByReferenceAsync(
            request.Reference, request.Source, context.CancellationToken));

    public override async Task<GetTrackingResponse> GetTracking(
        GetTrackingRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DeliveryId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "delivery_id n'est pas un GUID."));
        }

        var suivi = await _deliveries.GetTrackingAsync(id, context.CancellationToken);

        if (suivi is null)
        {
            return new GetTrackingResponse { Found = false };
        }

        var reponse = new GetTrackingResponse
        {
            Found = true,
            Status = suivi.Status,
            DriverName = suivi.DriverName ?? string.Empty,
            DriverPhone = suivi.DriverPhone ?? string.Empty
        };

        // La position n'est renseignée que PENDANT le transport.
        if (suivi.DriverLatitude is { } lat)
        {
            reponse.DriverLatitude = lat;
        }

        if (suivi.DriverLongitude is { } lon)
        {
            reponse.DriverLongitude = lon;
        }

        if (suivi.PositionReportedAtUtc is { } quand)
        {
            reponse.PositionReportedAt = quand.ToString("O", CultureInfo.InvariantCulture);
        }

        return reponse;
    }

    public override async Task<ResolveDriverResponse> ResolveDriver(
        ResolveDriverRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DriverId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "driver_id n'est pas un GUID."));
        }

        var compte = await _deliveries.GetDriverAccountAsync(id, context.CancellationToken);

        return compte is null
            ? new ResolveDriverResponse { Found = false }
            : new ResolveDriverResponse
            {
                Found = true,
                DriverId = compte.DriverId.ToString(),
                UserId = compte.UserId.ToString(),
                FullName = compte.FullName
            };
    }

    // ── Conversions ────────────────────────────────────────────────────────

    private static GetDeliveryResponse Reponse(Contracts.DeliverySummary? course)
        => course is null
            ? new GetDeliveryResponse { Found = false }
            : new GetDeliveryResponse
            {
                Found = true,
                Delivery = ToProto(course)
            };

    private static ProtoSummary ToProto(Contracts.DeliverySummary c)
    {
        var message = new ProtoSummary
        {
            DeliveryId = c.Id.ToString(),
            Reference = c.Reference,
            Source = c.Source,
            Type = c.Type,
            Status = c.Status,
            PickupSummary = c.PickupSummary,
            DropoffSummary = c.DropoffSummary,
            DriverName = c.DriverName ?? string.Empty,
            DriverPhone = c.DriverPhone ?? string.Empty,
            CreatedAt = c.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        if (c.AcceptedAtUtc is { } a)
        {
            message.AcceptedAt = a.ToString("O", CultureInfo.InvariantCulture);
        }

        if (c.PickedUpAtUtc is { } p)
        {
            message.PickedUpAt = p.ToString("O", CultureInfo.InvariantCulture);
        }

        if (c.DeliveredAtUtc is { } d)
        {
            message.DeliveredAt = d.ToString("O", CultureInfo.InvariantCulture);
        }

        return message;
    }

    private static DeliveryStopInput Arret(ProtoStop stop)
        => new(
            Vide(stop.ContactName),
            Vide(stop.Phone),
            Vide(stop.Commune),
            Vide(stop.Quartier),
            Vide(stop.Landmark),
            Vide(stop.Instructions),
            stop.HasLatitude ? stop.Latitude : null,
            stop.HasLongitude ? stop.Longitude : null);

    /// <summary>Traduit une chaîne en valeur d'énumération, ou REFUSE.</summary>
    private static T Enumeration<T>(string valeur, string champ) where T : struct, Enum
        => Enum.TryParse<T>(valeur, ignoreCase: true, out var resultat)
            ? resultat
            : throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"{champ} : « {valeur} » n'est pas une valeur connue de {typeof(T).Name}."));

    private static string? Vide(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Montant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Un montant venu du fil.</summary>
    private static decimal Montant(
        string value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);

    private static DateTime? Horodatage(string value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
