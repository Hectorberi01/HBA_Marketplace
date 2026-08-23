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
        // Le panier n'a pas de rôle : il a un propriétaire. Chaque gestionnaire
        // tire l'acheteur du jeton et le passe à la commande, si bien qu'aucune
        // route ne peut désigner le panier d'autrui. La seule qui accepte un
        // identifiant dans l'URL vérifie à qui il appartient.
        var cart = app.MapAuthenticatedGroup("/api/food/cart").WithTags("Food · Panier");

        cart.MapGet("/", GetActiveAsync);
        cart.MapGet("/{id:guid}", GetByIdAsync);
        cart.MapPost("/items", AddItemAsync);

        // PAR LA LIGNE, ET JAMAIS PAR LE PLAT.
        //
        // Le panier de la marketplace exposait `/items/{offerId}` pour la
        // quantité et le retrait. Transposé aux repas, ce chemin serait ambigu :
        // le même plat peut figurer deux fois, une fois avec du poulet et une
        // fois sans, et l'identifiant du plat ne dit pas laquelle viser. Ces
        // routes-là n'existent donc pas ici.
        cart.MapPut("/lines/{lineId:guid}", UpdateLineQuantityAsync);
        cart.MapDelete("/lines/{lineId:guid}", RemoveLineAsync);

        cart.MapDelete("/", ClearAsync);
        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", RemoveCouponAsync);

        // IL N'Y A PAS DE ROUTE `/checkout` ICI, ET C'EST DÉLIBÉRÉ.
        //
        // Passer commande, c'est créer une commande : cela appartient à
        // food-order-service, qui lit ce panier par gRPC — `POST /api/food/orders`.
        // Le panier se clôt ensuite, en écoutant « commande passée ».
        //
        // Le panier de la marketplace fait l'inverse : son `POST /checkout`
        // appelle Ordering et rend `/api/orders/{id}`. Le résultat est un panier
        // qui sait créer une commande, c'est-à-dire une dépendance dans le sens
        // où elle n'a pas de raison d'être.

        return app;
    }

    private static async Task<IResult> GetActiveAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetActiveFoodCartQuery(buyerId), ct)).Match(cart => Results.Ok(cart));

    /// <summary>
    /// Un panier par son identifiant — celui de l'appelant, et pas un autre.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CONTRÔLE EST ICI, PAS DANS LA REQUÊTE.
    ///
    /// La même requête sert `FoodCartModuleApi`, c'est-à-dire l'appel gRPC de
    /// food-order-service au moment de commander, où il n'y a pas d'acheteur
    /// connecté à comparer. Poser la vérification dans le gestionnaire casserait
    /// le passage en commande.
    ///
    /// « Introuvable » et non « interdit » : un 403 confirmerait que le panier
    /// existe. Son propriétaire, lui, ne voit jamais la différence.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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

    /// <summary>
    /// Ajoute un plat au panier.
    /// </summary>
    /// <remarks>
    /// LE CORPS NE PORTE NI PRIX NI DEVISE, ET C'EST TOUT LE POINT.
    ///
    /// `POST /api/commerce/cart/food-items` acceptait `unitBaseAmount` et
    /// `currency` depuis le client. Ils sont lus dans la carte du restaurant.
    /// </remarks>
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
