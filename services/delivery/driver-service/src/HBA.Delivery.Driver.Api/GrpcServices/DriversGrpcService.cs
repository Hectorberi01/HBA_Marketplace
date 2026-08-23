using System.Globalization;
using Grpc.Core;
using HBA.Drivers.Application.Accounts;
using HBA.Drivers.Application.Accounts.Queries;
using HBA.Drivers.Grpc.V1;
using MediatR;

namespace HBA.Drivers.Api.Grpc;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PORT INTERNE DU DOSSIER LIVREUR.
///
/// IL LISAIT UN `ConcurrentDictionary` ; IL LIT MAINTENANT LA BASE. C'est le
/// même changement que partout dans ce lot, et c'est celui qui compte : deux
/// réplicas de ce service donnaient auparavant deux réponses différentes à la
/// même question, sans que rien ne le signale.
///
/// CE PORT N'A AUCUN APPELANT AUJOURD'HUI, et c'est un fait à garder en tête :
/// aucun service du dépôt n'enregistre `AddDriversGrpcClient`. Le raccordement de
/// delivery-service au dossier passe par l'ÉVÉNEMENT `driver.dossier-verified`,
/// pas par ce port. Il reste écrit parce que le contrat existe et que
/// l'éligibilité se posera synchroniquement le jour où dispatch-service sera réel.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DriversGrpcService : DriverApi.DriverApiBase
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
        //
        // `IDriverAccountRepository` n'expose pas de lecture par lot, parce que ce
        // port n'a aucun appelant : écrire la lecture groupée maintenant serait
        // optimiser un chemin que personne n'emprunte. Le jour où il en aura un,
        // c'est la première chose à corriger — le dispatch fait exactement cette
        // erreur-là chez delivery-service et l'a réparée avec `ListByIdsAsync`.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE OPÉRATION A CHANGÉ DE PROPRIÉTAIRE, ET ELLE REFUSE DE MENTIR.
    ///
    /// « Occupé » est un état d'EXPLOITATION : il dit qu'un livreur porte une
    /// course. Il vit dans `deliveries.drivers`, où `Driver.MarkBusy` et
    /// `Driver.CompleteMission` l'écrivent au fil des transitions de la course —
    /// c'est-à-dire au seul endroit qui sache quand il change.
    ///
    /// L'implémentation précédente écrivait cet état dans le dictionnaire de ce
    /// service. Elle rendait donc `found = true` à l'appelant pendant que le
    /// dispatch, qui lit l'autre table, ne voyait rien. Un refus explicite vaut
    /// mieux qu'un succès sans effet : le premier se corrige, le second se
    /// diagnostique après avoir mobilisé deux livreurs sur un colis.
    ///
    /// NE PAS LA RÉIMPLÉMENTER ICI. Le geste correct, le jour où un appelant
    /// en aura besoin, est d'exposer l'opération sur le contrat de
    /// delivery-service.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public override Task<SetBusyStateResponse> SetBusyState(
        HBA.Drivers.Grpc.V1.SetBusyStateRequest request, ServerCallContext context)
        => throw new RpcException(new Status(
            StatusCode.Unimplemented,
            "L'état « occupé » d'un livreur appartient à delivery-service (deliveries.drivers) :"
            + " il est écrit par les transitions de la course, pas par le dossier livreur."));

    private static HBA.Drivers.Grpc.V1.DriverProfile ToProto(DriverAccountDto account)
    {
        // LE CONTRAT DEMANDE UN PRÉNOM ET UN NOM ; LE DOMAINE N'EN CONNAÎT QU'UN.
        //
        // `DriverAccount.FullName` est un seul champ, comme `Driver.FullName` chez
        // delivery-service — parce qu'au Bénin la décomposition prénom/nom n'est
        // pas fiable et que rien dans la plateforme n'en dépend. La découpe se fait
        // donc ICI, au bord, et elle est APPROXIMATIVE : ce qui suit le premier
        // espace part dans le nom. Aucune décision ne s'appuie dessus.
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

            // AUCUNE NOTE N'EST CALCULÉE NULLE PART DANS LA PLATEFORME. La
            // maquette rendait « 4,8 », une valeur inventée que le contrat
            // présentait comme un fait. Zéro est faux aussi, mais il ne se fait pas
            // passer pour une mesure.
            Rating = "0",
            CreatedAt = account.RegisteredAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };
    }
}
