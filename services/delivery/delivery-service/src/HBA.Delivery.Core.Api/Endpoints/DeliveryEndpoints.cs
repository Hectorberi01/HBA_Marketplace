using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Deliveries.Queries;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Deliveries.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Delivery.</summary>
public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var deliveries = app.MapAuthenticatedGroup("/api/deliveries").WithTags("Delivery · Deliveries");

        // ═════════════════════════════════════════════════════════════════════
        // LES COURSES PASSENT À L'EXPLOITATION — C'EST LA FUITE DÉCRITE DANS
        // `MapOperationsGroup`, ENCORE OUVERTE ICI.
        //
        // LES TROIS ROUTES PASSENT `RequiredPartnerId: null`, ET C'EST
        // LÉGITIME : le contrôle d'appartenance ne vaut que pour les
        // PARTENAIRES, et il rend « autorisé » dès que l'identifiant est absent.
        // La garde ne pouvait donc venir que du groupe, et le groupe n'exigeait
        // qu'un jeton.
        //
        // Avec un identifiant de course glané dans un ticket de support ou une
        // capture d'écran, tout inscrit obtenait le nom et le téléphone du
        // destinataire, les repères de son domicile, ceux du livreur, et sa
        // POSITION GPS EN DIRECT. Il annulait aussi la course d'un partenaire
        // payant : le colis restait chez le vendeur.
        //
        // LA CRÉATION NE PASSE PAS PAR ICI DANS LE FLUX NOMINAL.
        //
        // order-service et food-service créent leurs courses par gRPC
        // (`DeliveryGrpcService`, port interne). Cette route HTTP n'a pas
        // d'appelant applicatif : ouverte, elle laissait n'importe qui lancer
        // des courses vers des adresses arbitraires et mobiliser des livreurs.
        //
        // Exploitation et non simple administration : dépêcher, suivre et
        // débloquer une course est le métier du dispatcheur. La modération de
        // contenu n'a rien à faire dans le carnet d'adresses des clients.
        // ═════════════════════════════════════════════════════════════════════
        var deliveryOps = app.MapOperationsGroup("/api/deliveries").WithTags("Delivery · Exploitation");
        deliveryOps.MapPost("/", CreateDeliveryAsync).WithName("CreateDelivery");
        deliveryOps.MapGet("/{id:guid}", GetDeliveryAsync).WithName("GetDelivery");
        deliveryOps.MapGet("/{id:guid}/tracking", GetTrackingAsync).WithName("GetDeliveryTracking");
        deliveryOps.MapPost("/{id:guid}/cancel", CancelDeliveryAsync).WithName("CancelDelivery");

        return app;
    }

    private static async Task<IResult> CreateDeliveryAsync(CreateDeliveryRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateDeliveryCommand(
            request.Reference,
            request.Source,
            request.Type,
            request.Pickup,
            request.Dropoff,
            request.Package,
            request.DeclaredValue,
            request.IsCashOnDelivery,
            request.PartnerId,
            request.QuoteId,
            request.ScheduledForUtc), ct))
            .Match(id => Results.Created($"/api/deliveries/{id}", new { id }));

    private static async Task<IResult> GetDeliveryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDeliveryQuery(id, RequiredPartnerId: null), ct)).Match(item => Results.Ok(item));

    private static async Task<IResult> GetTrackingAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDeliveryTrackingQuery(id, RequiredPartnerId: null), ct)).Match(item => Results.Ok(item));

    private static async Task<IResult> CancelDeliveryAsync(Guid id, CancelDeliveryRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelDeliveryCommand(id, request.Reason, RequiredPartnerId: null), ct))
            .Match(() => Results.NoContent());

    public sealed record CreateDeliveryRequest(
        string Reference,
        DeliverySource Source,
        DeliveryType Type,
        DeliveryStopInput Pickup,
        DeliveryStopInput Dropoff,
        DeliveryPackageInput Package,

        // « RequiredProof » A DISPARU DE CETTE REQUÊTE — ISSUE-057.
        //
        // Le demandeur ne choisit plus sa preuve : il déclare ce qu'il envoie,
        // et `ProofPolicy` tranche dans le domaine. Un partenaire externe qui
        // choisissait « None » obtenait une course clôturable sans rien.
        decimal? DeclaredValue = null,
        bool IsCashOnDelivery = false,
        Guid? PartnerId = null,
        string? QuoteId = null,
        DateTime? ScheduledForUtc = null);

    public sealed record CancelDeliveryRequest(string? Reason);
}
