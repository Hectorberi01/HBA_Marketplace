using System.Security.Claims;
using HBA.Food.Contracts;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.FoodOrders.Application.Orders.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.FoodOrders.Api.Endpoints;

/// <summary>Surface HTTP des commandes de repas.</summary>
public static class MealOrderEndpoints
{
    public static IEndpointRouteBuilder MapMealOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var client = app.MapAuthenticatedGroup("/api/food/orders").WithTags("Food · Commandes");
        client.MapGet("/", ListMineAsync);
        client.MapGet("/{id:guid}", GetAsync);
        client.MapPost("/", PlaceAsync);
        client.MapPost("/{id:guid}/cancel", CancelAsync);

        // L'ESPACE RESTAURATEUR.
        var restaurateur = app
            .MapAuthenticatedGroup("/api/food/restaurant/orders")
            .WithTags("Food · Commandes (restaurateur)");
        restaurateur.MapGet("/", ListForMyRestaurantAsync);

        // LA FILE D'ARBITRAGE.
        var admin = app.MapAdminGroup("/api/admin/food/orders").WithTags("Food · Commandes (admin)");
        admin.MapPost("/{id:guid}/review/resume", ResumeAfterReviewAsync);
        admin.MapPost("/{id:guid}/review/refund", RefundAfterReviewAsync);

        return app;
    }

    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyMealOrdersQuery(buyerId), ct)).Match(Results.Ok);

    /// <summary>Une commande — la sienne, et pas une autre.</summary>
    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var demandeur = user.IsInRole(ApiAuthorization.AdminRole) ? (Guid?)null : buyerId;
        return (await sender.Send(new GetMealOrderQuery(id, demandeur), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> PlaceAsync(
        ClaimsPrincipal user, PlaceMealOrderRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new PlaceMealOrderCommand(
                buyerId,
                request.ShippingAddress,
                request.DeliveryQuoteId,
                request.CustomerNote), ct))
                .Match(id => Results.Created($"/api/food/orders/{id}", new { id }));

    private static async Task<IResult> CancelAsync(
        Guid id, ClaimsPrincipal user, CancelRequest? request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(
                new CancelMealOrderCommand(
                    id,
                    request?.Reason ?? "Annulée par le client.",
                    buyerId),
                ct))
                .Match(() => Results.NoContent());

    /// <summary>Les commandes de MON établissement.</summary>
    private static async Task<IResult> ListForMyRestaurantAsync(
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var appartenance = await food.GetStaffMembershipAsync(userId, ct);
        if (appartenance is null)
        {
            return Results.Forbid();
        }

        return (await sender.Send(
            new ListMealOrdersByRestaurantQuery(appartenance.RestaurantId), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> ResumeAfterReviewAsync(
        Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResumeMealOrderAfterReviewCommand(id), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> RefundAfterReviewAsync(
        Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(
            new RefundMealOrderAfterReviewCommand(
                id, request?.Reason ?? "Retour décidé après arbitrage."), ct))
            .Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var brut = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(brut, out var id) ? id : null;
    }

    /// <summary>AUCUN `ShippingFee` DANS CE CORPS, ET C'EST LA CORRECTION ELLE-MÊME.</summary>
    public sealed record PlaceMealOrderRequest(
        ShippingAddressInput? ShippingAddress,
        string? DeliveryQuoteId,
        string? CustomerNote);

    public sealed record CancelRequest(string? Reason);
}
