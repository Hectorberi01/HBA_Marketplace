using HBA.Shared.Hosting.Grpc;
using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Domain.Deliveries;

// `VehicleType` vit avec le livreur, pas avec la tarification — c'est une
// caractéristique de qui transporte, pas de ce qu'on facture.
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Grpc.V1;
using MediatR;

// TROIS ESPACES DE NOMS DÉCLARENT « DeliveryStop », ET DEUX « DeliverySummary ».
//
// Le proto, le domaine et les contrats publics nomment naturellement les mêmes
// concepts pareil. Sans alias, le compilateur choisit — et il choisit mal :
// `DeliveryStop` résoudrait vers l'objet du DOMAINE, que ce fichier n'a aucune
// raison de manipuler. Les alias rendent la frontière visible à la lecture.
using Contracts = HBA.Deliveries.Contracts;
using ProtoStop = HBA.Deliveries.Grpc.V1.DeliveryStop;
using ProtoSummary = HBA.Deliveries.Grpc.V1.DeliverySummary;

namespace HBA.Deliveries.Api.Grpc;

/// <summary>
/// Le moteur logistique, servi à ses donneurs d'ordre.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INVARIANT DE `IDeliveryModuleApi` EST PRÉSERVÉ, PAS CONTOURNÉ.
///
/// Ce contrat déclare : « créer une course passe par une commande MediatR — un
/// autre module ne doit pas pouvoir déclencher une livraison par un simple appel
/// de méthode, sans validation ni événement. »
///
/// C'est exactement ce qui se passe ici : `CreateDelivery` construit un
/// `CreateDeliveryCommand` et le passe à MediatR. Validation FluentValidation,
/// règles du domaine — commune connue, téléphone béninois, repère obligatoire —
/// et publication de `DeliveryCreatedIntegrationEvent` ont lieu comme par la
/// route REST. Seul le transport change.
///
/// POURQUOI CE FICHIER N'EST PAS DANS `shared/contracts`.
///
/// Les autres serveurs gRPC y vivent parce qu'ils ne font que lire, via un
/// `IXxxModuleApi` sans dépendance. Celui-ci a besoin de MediatR et de la couche
/// Application de delivery-service. L'y placer ferait dépendre le socle partagé
/// de l'intérieur d'un service, et tout appelant du client hériterait de cette
/// dépendance.
///
/// LES ENTRÉES SONT DES CHAÎNES, ET ELLES SONT VALIDÉES ICI.
///
/// Le contrat proto ne connaît pas les énumérations du domaine — il ne doit pas
/// les connaître, sous peine de rendre chaque ajout de valeur incompatible avec
/// les clients déjà déployés. La traduction se fait donc à la frontière, et une
/// valeur inconnue est REFUSÉE plutôt que ramenée à un défaut : « HbaFood » mal
/// orthographié deviendrait sinon silencieusement « HbaExpress », et la course
/// partirait dans le mauvais flux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class DeliveryGrpcService : DeliveryApi.DeliveryApiBase
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

        // `required_proof` A ÉTÉ RETIRÉ DU CONTRAT — ISSUE-057. La preuve
        // n'est plus lue de la requête : `ProofPolicy` la déduit dans le domaine
        // de ce que la course déclare transporter.
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
        //
        // Commune inconnue, téléphone invalide, quota partenaire atteint : ce
        // sont des réponses fréquentes et attendues. Les rendre en RpcException
        // obligerait chaque appelant à distinguer « refusé » de « le service est
        // tombé » en lisant un code de statut.
        //
        // LE CODE ET LE MESSAGE VOYAGENT DANS DEUX CHAMPS, PLUS DANS UN SEUL.
        //
        // `$"{Code} — {Message}"` empaquetait les deux dans `reason`. Personne ne
        // le reparsait — ni ici, ni chez `FinancialGrpcService`, qui empaquetait
        // d'ailleurs avec « : » au lieu de « — ». Le code normalisé, seul élément
        // stable, était donc perdu au saut gRPC.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DONNEUR D'ORDRE ANNULE SA COURSE.
    ///
    /// ON RÉSOUT LA RÉFÉRENCE ICI, PAS CHEZ L'APPELANT.
    ///
    /// Il ne connaît que « ORDER-… » ; l'identifiant de course, il ne l'a jamais
    /// stocké. Lui faire enchaîner une lecture par référence puis une annulation
    /// ferait deux allers-retours réseau là où l'un suffit — sur un chemin où
    /// chaque minute est un trajet de livreur déjà engagé.
    ///
    /// « INTROUVABLE » N'EST PAS UNE ERREUR, ET UN REFUS NON PLUS.
    ///
    /// La plupart des commandes sont annulées AVANT confirmation, donc avant
    /// qu'aucune course n'existe : c'est le cas le plus fréquent. Et un colis
    /// déjà collecté ne s'annule plus — le domaine le refuse, à juste titre.
    /// Rendre l'un ou l'autre en `RpcException` obligerait l'appelant à lire un
    /// code de statut pour distinguer « rien à faire » de « le service est
    /// tombé ».
    ///
    /// `RequiredPartnerId: null` EST UN CHOIX ÉCRIT, PAS UN OUBLI.
    ///
    /// La valeur par défaut a été retirée de `CancelDeliveryCommand` précisément
    /// pour que ce choix ne puisse plus être fait par distraction : la surface
    /// gRPC interne n'est joignable qu'avec la clé de service à service, et un
    /// donneur d'ordre HBA ne peut désigner que ses propres références.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

        // La position n'est renseignée que PENDANT le transport. Ce n'est pas
        // une optimisation : suivre en continu la position d'une personne en
        // dehors de la mission qui la justifie serait une collecte sans finalité.
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

    /// <summary>
    /// Traduit une chaîne en valeur d'énumération, ou REFUSE.
    /// </summary>
    /// <remarks>
    /// PAS DE REPLI SUR LA VALEUR PAR DÉFAUT.
    ///
    /// `Enum.TryParse` rendrait `false` et laisserait tenté de retomber sur la
    /// première valeur. « HbaFood » mal orthographié deviendrait « HbaExpress »,
    /// et la course partirait dans le mauvais flux sans que rien ne le dise.
    /// </remarks>
    private static T Enumeration<T>(string valeur, string champ) where T : struct, Enum
        => Enum.TryParse<T>(valeur, ignoreCase: true, out var resultat)
            ? resultat
            : throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"{champ} : « {valeur} » n'est pas une valeur connue de {typeof(T).Name}."));

    private static string? Vide(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Montant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Un montant venu du fil.
    /// </summary>
    /// <remarks>
    /// REFUSAIT DE RENDRE ZÉRO — voir <see cref="MontantSurLeFil"/>. Cette
    /// fonction s'écrivait « TryParse(…) ? valeur : 0m », comme six autres du
    /// dépôt : un champ non posé par l'émetteur — donc la chaîne VIDE, il n'y a
    /// pas de « non renseigné » pour un `string` protobuf 3 — se lisait « zéro
    /// franc ».
    ///
    /// `champ` EST REMPLI PAR LE COMPILATEUR, pas à la main. Il reçoit le TEXTE
    /// de l'expression passée — « order.AlreadyRefundedAmount » — donc un nom plus
    /// précis qu'aucun littéral recopié, et qui suit les renommages tout seul.
    /// </remarks>
    private static decimal Montant(
        string value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);

    private static DateTime? Horodatage(string value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
