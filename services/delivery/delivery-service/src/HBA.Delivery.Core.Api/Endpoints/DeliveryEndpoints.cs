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

        // LES COURSES PASSENT À L'EXPLOITATION — C'EST LA FUITE DÉCRITE DANS
        // `MapOperationsGroup`, ENCORE OUVERTE ICI.
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
        decimal? DeclaredValue = null,
        bool IsCashOnDelivery = false,
        Guid? PartnerId = null,
        string? QuoteId = null,
        DateTime? ScheduledForUtc = null);

    public sealed record CancelDeliveryRequest(string? Reason);
}
