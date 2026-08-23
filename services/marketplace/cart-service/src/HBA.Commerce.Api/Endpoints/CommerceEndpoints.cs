using System.Security.Claims;
using HBA.Commerce.Application.Carts.Commands;
using HBA.Commerce.Application.Carts.Commands.AddItem;
using HBA.Commerce.Application.Carts.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Commerce.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Commerce.</summary>
public static class CommerceEndpoints
{
    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder app)
    {
        // Le panier n'a pas de rôle : il a un propriétaire. Chaque handler tire
        // l'acheteur du jeton — `CurrentUserId` — et le passe à la commande, si
        // bien qu'aucune route ne peut désigner le panier d'autrui. La seule qui
        // acceptait un identifiant dans l'URL est `GetByIdAsync` ; elle vérifie
        // désormais à qui il appartient.
        var cart = app.MapAuthenticatedGroup("/api/commerce/cart").WithTags("Commerce · Cart");
        cart.MapGet("/", GetActiveAsync);
        cart.MapGet("/{id:guid}", GetByIdAsync);
        cart.MapPost("/items", AddItemAsync);

        // ═════════════════════════════════════════════════════════════════════
        // `POST /food-items` EST RETIRÉE (lot 6.4). DEUX RAISONS, ET LA
        // SECONDE SUFFIRAIT SEULE.
        //
        // 1. ELLE FAISAIT COEXISTER DEUX CHAÎNES DE RESTAURATION.
        //
        //    Un plat ajouté ici devenait une ligne `CartLineKind.Food`, puis une
        //    commande order-service, puis un ticket de cuisine. Le MÊME ticket
        //    naît aussi du parcours dédié — `POST /api/food/cart/items` puis
        //    `POST /api/food/orders`. Les deux écrivaient leur identifiant de
        //    commande dans le même champ `FoodOrder.OrderId`, sans discriminant :
        //    six gestionnaires inter-services lisaient ce champ nu et
        //    interrogeaient leur propre base avec. Voir `FoodOrderOrigin`.
        //
        // 2. LE PRIX VENAIT DU CORPS DE LA REQUÊTE.
        //
        //    `AddFoodItemRequest.UnitBaseAmount` — un client commandait à SON
        //    prix. Le parcours dédié lit la carte chez restaurant-service
        //    (`IFoodModuleApi.GetMenuItemAsync`) et ne demande jamais le montant.
        //    Fermer cette porte ferme aussi cette faille.
        //
        // AUCUN APPELANT. Balayage de `clients/` et `apps/` : zéro occurrence
        // de « food-items ». Le retrait ne casse aucune application déployée.
        //
        // CE QUI RESTE, ET QU'IL FAUDRA RETIRER ENSUITE.
        //
        // `AddFoodItemToCartCommand`, `Cart.AddFoodItem`, `CartLineKind.Food`,
        // `CartItemOption` et leurs colonnes restent en place : des paniers et des
        // commandes EXISTANTS les portent, et les effacer réécrirait l'histoire de
        // commandes déjà livrées. Ils ne sont plus atteignables de l'extérieur —
        // c'est ce que ce lot garantit — mais ils ne sont pas encore partis.
        // ═════════════════════════════════════════════════════════════════════
        cart.MapPut("/items/{offerId:guid}", UpdateItemQuantityAsync);
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync);
        cart.MapPut("/lines/{lineId:guid}", UpdateLineQuantityAsync);
        cart.MapDelete("/lines/{lineId:guid}", RemoveLineAsync);
        cart.MapDelete("/", ClearAsync);
        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", RemoveCouponAsync);

        // ═════════════════════════════════════════════════════════════════════
        // `POST /checkout` A ÉTÉ RETIRÉE — ELLE EMPÊCHAIT D'ACHETER.
        //
        // Elle répondait :
        //
        //     201 Created   Location: /api/orders/{orderId}
        //
        // où `orderId` était en réalité L'IDENTIFIANT DU PANIER.
        // `CheckoutCartCommandHandler` marquait le panier `CheckedOut`, sauvait,
        // et rendait `cart.Id.Value`. Aucune commande n'était créée nulle part :
        // ni appel à Ordering, ni événement écouté — `CartCheckedOut` n'a aucun
        // consommateur enregistré dans le dépôt.
        //
        // Le dommage n'était pas seulement de mentir sur le résultat. En clôturant
        // le panier, la route le rendait invisible à `GetActiveByBuyerAsync` : le
        // `POST /api/orders` qui aurait dû suivre échouait sur `ordering.cart_empty`.
        // Un client qui suivait l'en-tête `Location` obtenait un 404, et un client
        // qui enchaînait correctement se retrouvait sans panier.
        //
        // CE N'EST PAS AU PANIER DE CRÉER UNE COMMANDE.
        //
        // Le chemin nominal est `POST /api/orders`, servi par order-service, qui
        // lit ce panier par gRPC et le fige. Le panier se clôt ensuite, en écoutant
        // `OrderPlaced` — voir `CloseCartOnOrderPlacedHandler`. C'est la même
        // frontière que food-cart-service, qui n'expose délibérément aucun
        // `/checkout`.
        //
        // `Cart.MarkCheckedOut` reste : c'est la chorégraphie qui l'appelle.
        // ═════════════════════════════════════════════════════════════════════

        return app;
    }

    private static async Task<IResult> GetActiveAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetActiveCartQuery(buyerId), ct)).Match(cart => Results.Ok(cart));

    /// <summary>
    /// Un panier par son identifiant — celui de l'appelant, et pas un autre.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA SEULE ROUTE DU PANIER QUI PRENAIT SON SUJET DANS L'URL.
    ///
    /// Les onze autres tirent l'acheteur du jeton. Celle-ci acceptait
    /// n'importe quel identifiant de panier et le rendait valorisé : contenu
    /// ligne à ligne, montants, code promotionnel appliqué. L'identifiant n'est
    /// pas secret — il est rendu en clair par `POST /items` dans l'en-tête
    /// `Location` et dans le corps, donc partagé, journalisé, mis en cache.
    ///
    /// Le contrôle se fait ICI et non dans `GetCartByIdQuery` : la même requête
    /// sert `CartModuleApi`, c'est-à-dire l'appel gRPC d'order-service au
    /// moment du paiement, où il n'y a pas d'acheteur connecté à comparer.
    /// Poser la vérification dans le handler casserait le passage en commande.
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
