using Grpc.Core;
using HBA.Drivers.Application.Accounts.Queries;
using HBA.Drivers.Application.Accounts;
using HBA.Drivers.Grpc.V1;
using MediatR;

using System.Globalization;

// DEPLACE DEPUIS `HBA.Drivers.Api.Grpc` (lot B de la migration gRPC).

namespace HBA.Drivers.Api.Grpc.Services;

/// <summary>LE PORT INTERNE DU DOSSIER LIVREUR.</summary>
internal sealed class DriversGrpcService : DriverApi.DriverApiBase
{
    private readonly ISender _sender;

    public DriversGrpcService(ISender sender)
    {
        _sender = sender;
    }

    public override async Task<GetDriverResponse> GetDriver(GetDriverRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DriverId, out var driverId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "driver_id invalide."));
        }

        var account = await _sender.Send(new GetDriverAccountQuery(driverId), context.CancellationToken);

        return account.IsSuccess
            ? new GetDriverResponse { Found = true, Driver = ToProto(account.Value) }
            : new GetDriverResponse { Found = false };
    }

    public override async Task<GetDriversBatchResponse> GetDriversBatch(
        GetDriversBatchRequest request, ServerCallContext context)
    {
        var response = new GetDriversBatchResponse();

        // UNE REQUÊTE PAR IDENTIFIANT, ET C'EST UN DÉFAUT ASSUMÉ.
        foreach (var id in request.DriverIds)
        {
            if (!Guid.TryParse(id, out var driverId))
            {
                continue;
            }

            var account = await _sender.Send(new GetDriverAccountQuery(driverId), context.CancellationToken);
            if (account.IsSuccess)
            {
                response.Drivers.Add(ToProto(account.Value));
            }
        }

        return response;
    }

    public override async Task<DriverEligibilityResponse> CheckDriverEligibility(
        HBA.Drivers.Grpc.V1.CheckDriverEligibilityRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.DriverId, out var driverId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "driver_id invalide."));
        }

        var query = new CheckDriverEligibilityQuery(
            driverId, request.HasRequiredVehicleType ? request.RequiredVehicleType : null);

        var eligibility = await _sender.Send(query, context.CancellationToken);
        if (eligibility.IsFailure)
        {
            throw new RpcException(new Status(StatusCode.Internal, eligibility.Error.Message));
        }

        var response = new DriverEligibilityResponse
        {
            DriverId = eligibility.Value.DriverId.ToString(),
            Eligible = eligibility.Value.Eligible
        };

        if (!string.IsNullOrWhiteSpace(eligibility.Value.Reason))
        {
            response.Reason = eligibility.Value.Reason;
        }

        return response;
    }

    /// <summary>CETTE OPÉRATION A CHANGÉ DE PROPRIÉTAIRE, ET ELLE REFUSE DE MENTIR.</summary>
    public override Task<SetBusyStateResponse> SetBusyState(
        HBA.Drivers.Grpc.V1.SetBusyStateRequest request, ServerCallContext context)
        => throw new RpcException(new Status(
            StatusCode.Unimplemented,
            "L'état « occupé » d'un livreur appartient à delivery-service (deliveries.drivers) :"
            + " il est écrit par les transitions de la course, pas par le dossier livreur."));

    private static HBA.Drivers.Grpc.V1.DriverProfile ToProto(DriverAccountDto account)
    {
        // LE CONTRAT DEMANDE UN PRÉNOM ET UN NOM ; LE DOMAINE N'EN CONNAÎT QU'UN.
        var separateur = account.FullName.IndexOf(' ');

        return new HBA.Drivers.Grpc.V1.DriverProfile
        {
            Id = account.DriverId.ToString(),
            UserId = account.UserId.ToString(),
            Status = account.VerificationStatus,
            VerificationStatus = account.VerificationStatus,
            FirstName = separateur > 0 ? account.FullName[..separateur] : account.FullName,
            LastName = separateur > 0 ? account.FullName[(separateur + 1)..] : string.Empty,
            Phone = account.Phone,

            // AUCUNE NOTE N'EST CALCULÉE NULLE PART DANS LA PLATEFORME. La maquette
            // rendait « 4,8 », une valeur inventée que le contrat présentait comme
            // un fait.
            Rating = "0",
            CreatedAt = account.RegisteredAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };
    }
}
