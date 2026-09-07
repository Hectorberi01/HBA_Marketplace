using System.Security.Claims;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Reviews.Application.Reviews.Commands;
using HBA.Engagement.Reviews.Application.Reviews.Commands.SubmitReview;
using HBA.Engagement.Reviews.Application.Reviews.Queries;
using HBA.Engagement.Wishlist.Application.Wishlists;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Engagement.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Engagement.</summary>
public static class EngagementEndpoints
{
    public static IEndpointRouteBuilder MapEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapAuthenticatedGroup("/api/engagement/reviews").WithTags("Engagement · Reviews");
        reviews.MapGet("/{id:guid}", GetReviewAsync);
        reviews.MapGet("/product/{productId:guid}", ListReviewsByProductAsync);
        reviews.MapGet("/seller/{sellerId:guid}", ListReviewsBySellerAsync);
        reviews.MapGet("/product/{productId:guid}/rating", GetProductRatingAsync);
        reviews.MapGet("/seller/{sellerId:guid}/rating", GetSellerRatingAsync);
        // L'auteur vient du jeton, jamais du corps : voir `SubmitReviewAsync`.
        reviews.MapPost("/", SubmitReviewAsync);

        // LA RÉPONSE DU VENDEUR — UN TROISIÈME GROUPE SUR LE MÊME PRÉFIXE.
        var sellerReplies = app.MapSellerGroup("/api/engagement/reviews").WithTags("Engagement · Reviews · Vendeur");
        sellerReplies.MapPost("/{id:guid}/reply", ReplyToReviewAsync);

        // MODÉRATION — TROIS ROUTES QUI SUPPRIMAIENT LA CRITIQUE.
        var moderation = app.MapAdminGroup("/api/engagement/reviews").WithTags("Engagement · Modération");

        // ET IL MANQUAIT LA FILE, C'EST-À-DIRE CE QUI REND LES TROIS GESTES
        // UTILISABLES.
        moderation.MapGet("/moderation", ListReviewsForModerationAsync);

        moderation.MapPost("/{id:guid}/flag", FlagReviewAsync);
        moderation.MapPost("/{id:guid}/reject", RejectReviewAsync);
        moderation.MapPost("/{id:guid}/restore", RestoreReviewAsync);

        var recommendations = app.MapAuthenticatedGroup("/api/engagement/recommendations").WithTags("Engagement · Recommendations");
        recommendations.MapGet("/product/{productId:guid}", GetProductRecommendationsAsync);
        recommendations.MapGet("/me", GetMyRecommendationsAsync);
        recommendations.MapGet("/users/{userId:guid}", GetUserRecommendationsAsync);

        // ÉCRIRE UNE RECOMMANDATION, C'EST ÉCRIRE LA PAGE D'ACCUEIL.
        var recommendationsAdmin = app.MapAdminGroup("/api/engagement/recommendations")
            .WithTags("Engagement · Recommendations · Admin");

        // ON ÉCRIVAIT LA PAGE D'ACCUEIL SANS POUVOIR LA RELIRE.
        recommendationsAdmin.MapGet("/", ListRecommendationsAsync);

        recommendationsAdmin.MapPost("/", UpsertRecommendationAsync);

        var wishlist = app.MapAuthenticatedGroup("/api/engagement/wishlist").WithTags("Engagement · Wishlist");
        wishlist.MapGet("/", GetMyWishlistAsync);
        wishlist.MapPost("/items", AddToWishlistAsync);
        wishlist.MapPut("/items/{productId:guid}/alerts", SetWishlistAlertsAsync);
        wishlist.MapDelete("/items/{productId:guid}", RemoveFromWishlistAsync);

        return app;
    }

    private static async Task<IResult> GetReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetReviewQuery(id), ct)).Match(Results.Ok);

    private static async Task<IResult> ListReviewsByProductAsync(Guid productId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListReviewsByProductQuery(productId), ct)).Match(Results.Ok);

    private static async Task<IResult> ListReviewsBySellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListReviewsBySellerQuery(sellerId), ct)).Match(Results.Ok);

    private static async Task<IResult> GetProductRatingAsync(Guid productId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductRatingQuery(productId), ct)).Match(Results.Ok);

    private static async Task<IResult> GetSellerRatingAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSellerRatingQuery(sellerId), ct)).Match(Results.Ok);

    private static async Task<IResult> SubmitReviewAsync(ClaimsPrincipal user, SubmitReviewRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new SubmitReviewCommand(buyerId, request.ProductId, request.OrderId, request.Rating, request.Title, request.Body), ct))
                .Match(id => Results.Created($"/api/engagement/reviews/{id}", new { id }));

    /// <summary>Réponse publique du vendeur à un avis.</summary>
    private static async Task<IResult> ReplyToReviewAsync(
        Guid id, ClaimsPrincipal user, BodyRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } callerId
            ? Results.Unauthorized()
            : (await sender.Send(new ReplyToReviewCommand(id, callerId, request.Body), ct))
                .Match(() => Results.NoContent());

    /// <summary>La file de modération des avis (Admin ou Modérateur).</summary>
    private static async Task<IResult> ListReviewsForModerationAsync(
        int? page, int? pageSize, string? status, ISender sender, CancellationToken ct)
    {
        var demande = new ListReviewsForModerationQuery(Page: page ?? 1, Status: status);

        var resultat = await sender.Send(
            pageSize is { } taille ? demande with { PageSize = taille } : demande, ct);

        return resultat.Match(donnees => ApiResults.Page(donnees));
    }

    private static async Task<IResult> FlagReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new FlagReviewCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RejectReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectReviewCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RestoreReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RestoreReviewCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> GetProductRecommendationsAsync(Guid productId, string type, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductRecommendationsQuery(productId, type), ct)).Match(Results.Ok);

    private static async Task<IResult> GetMyRecommendationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUserRecommendationsQuery(userId), ct)).Match(Results.Ok);

    private static async Task<IResult> GetUserRecommendationsAsync(
        Guid userId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } callerId)
        {
            return Results.Unauthorized();
        }

        if (callerId != userId && !user.IsInRole(ApiAuthorization.AdminRole))
        {
            return Results.Forbid();
        }

        return (await sender.Send(new GetUserRecommendationsQuery(userId), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> ListRecommendationsAsync(
        int? page, int? pageSize, string? type, ISender sender, CancellationToken ct)
    {
        var demande = new ListRecommendationsQuery(Page: page ?? 1, Type: type);

        // `PageSize` n'est pas défaillé ici : la valeur par défaut vit dans le
        // record, et la lire depuis le projet Api lui imposerait une référence
        // directe sur `HBA.Shared.Application` pour une seule constante.
        var resultat = await sender.Send(
            pageSize is { } taille ? demande with { PageSize = taille } : demande, ct);

        return resultat.Match(donnees => ApiResults.Page(donnees));
    }

    private static async Task<IResult> UpsertRecommendationAsync(UpsertRecommendationCommand command, ISender sender, CancellationToken ct)
        => (await sender.Send(command, ct)).Match(id => Results.Created($"/api/engagement/recommendations/{id}", new { id }));

    private static async Task<IResult> GetMyWishlistAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetMyWishlistQuery(userId), ct)).Match(Results.Ok);

    private static async Task<IResult> AddToWishlistAsync(ClaimsPrincipal user, WishlistItemRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new AddToWishlistCommand(userId, request.ProductId, request.OfferId, request.PriceAlert, request.StockAlert), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SetWishlistAlertsAsync(Guid productId, ClaimsPrincipal user, WishlistAlertsRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SetWishlistAlertsCommand(userId, productId, request.PriceAlert, request.StockAlert), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveFromWishlistAsync(Guid productId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveFromWishlistCommand(userId, productId), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record SubmitReviewRequest(Guid ProductId, Guid OrderId, int Rating, string Title, string Body);
    public sealed record BodyRequest(string Body);
    public sealed record WishlistItemRequest(Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert);
    public sealed record WishlistAlertsRequest(bool PriceAlert, bool StockAlert);
}
