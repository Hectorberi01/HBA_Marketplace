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

        // ═════════════════════════════════════════════════════════════════════
        // L'ESPACE RESTAURATEUR.
        //
        // L'APPARTENANCE SE VÉRIFIE PAR LE PERSONNEL, PAS PAR LE PROPRIÉTAIRE.
        //
        // Résoudre l'établissement par son créateur revenait à interdire
        // l'application à tout le personnel — un manager, un caissier ou un
        // cuisinier n'avait accès à rien. `GetStaffMembershipAsync` rend
        // l'établissement ET les permissions du compte.
        // ═════════════════════════════════════════════════════════════════════
        var restaurateur = app
            .MapAuthenticatedGroup("/api/food/restaurant/orders")
            .WithTags("Food · Commandes (restaurateur)");
        restaurateur.MapGet("/", ListForMyRestaurantAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LA FILE D'ARBITRAGE.
        //
        // Une commande payée devenue inexécutable n'est ni annulée ni relancée
        // d'office : quelqu'un tranche. Voir `MealOrder.MarkUnderReview` pour la
        // raison — rembourser automatiquement détruirait des ventes récupérables,
        // et l'argent rendu ne se reprend pas.
        // ═════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// Une commande — la sienne, et pas une autre.
    /// </summary>
    /// <remarks>
    /// LE DEMANDEUR VOYAGE AVEC LA REQUÊTE, ET LA RÉPONSE EST « INTROUVABLE ».
    ///
    /// Un 403 confirmerait que la commande existe, et permettrait d'énumérer les
    /// commandes de la plateforme en essayant des identifiants. Le propriétaire,
    /// lui, ne voit jamais la différence.
    /// </remarks>
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

    /// <summary>
    /// Les commandes de MON établissement.
    /// </summary>
    /// <remarks>
    /// L'IDENTIFIANT DU RESTAURANT NE VIENT PAS DE L'URL.
    ///
    /// Le mettre dans le chemin obligerait à vérifier que l'appelant y travaille
    /// — un contrôle de plus, oubliable. Il est résolu depuis le jeton : il n'y a
    /// aucun identifiant à falsifier.
    /// </remarks>
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

    /// <summary>
    /// AUCUN `ShippingFee` DANS CE CORPS, ET C'EST LA CORRECTION ELLE-MÊME.
    ///
    /// `PlaceOrderRequest` en portait un : le client posait zéro, se faisait
    /// livrer gratuitement, et la plateforme achetait la course au prix réel.
    /// Seul l'identifiant du devis voyage ; le serveur en lit le montant.
    /// </summary>
    public sealed record PlaceMealOrderRequest(
        ShippingAddressInput? ShippingAddress,
        string? DeliveryQuoteId,
        string? CustomerNote);

    public sealed record CancelRequest(string? Reason);
}
