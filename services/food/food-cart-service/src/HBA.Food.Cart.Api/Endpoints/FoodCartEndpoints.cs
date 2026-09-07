using System.Security.Claims;
using HBA.FoodCarts.Application.Carts.Commands;
using HBA.FoodCarts.Application.Carts.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.FoodCarts.Api.Endpoints;

/// <summary>Surface HTTP du panier de restauration.</summary>
public static class FoodCartEndpoints
{
    public static IEndpointRouteBuilder MapFoodCartEndpoints(this IEndpointRouteBuilder app)
    {
        // Le panier n'a pas de rôle : il a un propriétaire.
        var cart = app.MapAuthenticatedGroup("/api/food/cart").WithTags("Food · Panier");

        cart.MapGet("/", GetActiveAsync);
        cart.MapGet("/{id:guid}", GetByIdAsync);
        cart.MapPost("/items", AddItemAsync);

        // PAR LA LIGNE, ET JAMAIS PAR LE PLAT.
        cart.MapPut("/lines/{lineId:guid}", UpdateLineQuantityAsync);
        cart.MapDelete("/lines/{lineId:guid}", RemoveLineAsync);

        cart.MapDelete("/", ClearAsync);
        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", RemoveCouponAsync);

        // IL N'Y A PAS DE ROUTE `/checkout` ICI, ET C'EST DÉLIBÉRÉ.

        return app;
    }

    private static async Task<IResult> GetActiveAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetActiveFoodCartQuery(buyerId), ct)).Match(cart => Results.Ok(cart));

    /// <summary>Un panier par son identifiant — celui de l'appelant, et pas un autre.</summary>
    private static async Task<IResult> GetByIdAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new GetFoodCartByIdQuery(id), ct))
            .Match(cart => cart.BuyerId == buyerId || user.IsInRole(ApiAuthorization.AdminRole)
                ? Results.Ok(cart)
                : Results.NotFound());
    }

    /// <summary>Ajoute un plat au panier.</summary>
    private static async Task<IResult> AddItemAsync(
        ClaimsPrincipal user, AddFoodItemRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new AddItemToFoodCartCommand(
                buyerId,
                request.RestaurantId,
                request.MenuItemId,
                request.Quantity,
                request.Notes,
                request.Options ?? []), ct))
                .Match(id => Results.Created($"/api/food/cart/{id}", new { id }));

    private static async Task<IResult> UpdateLineQuantityAsync(
        Guid lineId, ClaimsPrincipal user, QuantityRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateFoodCartLineQuantityCommand(buyerId, lineId, request.Quantity), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveLineAsync(
        Guid lineId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveFoodCartLineCommand(buyerId, lineId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ClearAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ClearFoodCartCommand(buyerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ApplyCouponAsync(
        ClaimsPrincipal user, CouponRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ApplyFoodCartCouponCommand(buyerId, request.Code), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveCouponAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveFoodCartCouponCommand(buyerId), ct))
                .Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var brut = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(brut, out var id) ? id : null;
    }

    public sealed record AddFoodItemRequest(
        Guid RestaurantId,
        Guid MenuItemId,
        int Quantity,
        string? Notes,
        IReadOnlyList<FoodOptionChoice>? Options);

    public sealed record QuantityRequest(int Quantity);

    public sealed record CouponRequest(string Code);
}
