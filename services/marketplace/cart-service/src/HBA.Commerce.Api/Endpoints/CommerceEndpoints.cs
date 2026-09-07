using System.Security.Claims;
using HBA.Commerce.Application.Carts.Commands;
using HBA.Commerce.Application.Carts.Commands.AddItem;
using HBA.Commerce.Application.Carts.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Commerce.Api.Endpoints;

public static class CommerceEndpoints
{
    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapAuthenticatedGroup("/api/commerce/cart").WithTags("Commerce · Cart");
        cart.MapGet("/", GetActiveAsync);
        cart.MapGet("/{id:guid}", GetByIdAsync);
        cart.MapPost("/items", AddItemAsync);
        cart.MapPut("/items/{offerId:guid}", UpdateItemQuantityAsync);
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync);
        cart.MapPut("/lines/{lineId:guid}", UpdateLineQuantityAsync);
        cart.MapDelete("/lines/{lineId:guid}", RemoveLineAsync);
        cart.MapDelete("/", ClearAsync);
        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", RemoveCouponAsync);
        return app;
    }

    private static async Task<IResult> GetActiveAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetActiveCartQuery(buyerId), ct)).Match(cart => Results.Ok(cart));

    
    private static async Task<IResult> GetByIdAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new GetCartByIdQuery(id), ct))
            .Match(cart => cart.BuyerId == buyerId || user.IsInRole(ApiAuthorization.AdminRole)
                ? Results.Ok(cart)
                : Results.NotFound());
    }

    private static async Task<IResult> AddItemAsync(
        ClaimsPrincipal user, AddItemRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new AddItemToCartCommand(buyerId, request.OfferId, request.Quantity), ct))
                .Match(id => Results.Created($"/api/commerce/cart/{id}", new { id }));

    private static async Task<IResult> UpdateItemQuantityAsync(
        Guid offerId, ClaimsPrincipal user, QuantityRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateCartItemQuantityCommand(buyerId, offerId, request.Quantity), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveItemAsync(Guid offerId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveCartItemCommand(buyerId, offerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UpdateLineQuantityAsync(
        Guid lineId, ClaimsPrincipal user, QuantityRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateCartLineQuantityCommand(buyerId, lineId, request.Quantity), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveLineAsync(Guid lineId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveCartLineCommand(buyerId, lineId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ClearAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ClearCartCommand(buyerId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ApplyCouponAsync(
        ClaimsPrincipal user, CouponRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ApplyCouponCommand(buyerId, request.Code), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RemoveCouponAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveCouponCommand(buyerId), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record AddItemRequest(Guid OfferId, int Quantity);
    public sealed record QuantityRequest(int Quantity);
    public sealed record CouponRequest(string Code);
}
