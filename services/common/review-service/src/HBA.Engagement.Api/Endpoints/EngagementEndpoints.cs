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

        // ═════════════════════════════════════════════════════════════════════
        // LA RÉPONSE DU VENDEUR — UN TROISIÈME GROUPE SUR LE MÊME PRÉFIXE.
        //
        // ELLE ÉTAIT DANS LE GROUPE « AUTHENTIFIÉ », DONC OUVERTE AUX ACHETEURS.
        //
        // `MapSellerGroup` n'est qu'une première barrière : il ne sait pas QUEL
        // vendeur parle, seulement qu'un rôle vendeur est présent. La
        // vérification qui compte — le porteur du jeton est-il le vendeur DE CET
        // AVIS — est dans `ReplyToReviewCommandHandler`, parce qu'elle a besoin
        // du `SellerId` de l'avis, donc de l'avoir chargé. La poser ici le ferait
        // lire deux fois.
        //
        // Le groupe laisse aussi passer `Admin` et `Moderator` ; le handler les
        // refuse, faute de dossier vendeur. C'est voulu, et dit sur place.
        // ═════════════════════════════════════════════════════════════════════
        var sellerReplies = app.MapSellerGroup("/api/engagement/reviews").WithTags("Engagement · Reviews · Vendeur");
        sellerReplies.MapPost("/{id:guid}/reply", ReplyToReviewAsync);

        // ═════════════════════════════════════════════════════════════════════
        // MODÉRATION — TROIS ROUTES QUI SUPPRIMAIENT LA CRITIQUE.
        //
        // `reject` RETIRE L'AVIS DE LA NOTE PUBLIQUE, ET N'IMPORTE QUI
        // POUVAIT L'APPELER.
        //
        // Les handlers ne lisaient aucune identité : ni auteur, ni vendeur, ni
        // rôle. Un jeton d'acheteur suffisait donc à faire disparaître l'avis à
        // une étoile qui gênait — le sien, celui d'un client mécontent chez un
        // concurrent, ou l'ensemble d'entre eux, un identifiant après l'autre.
        // `restore` republiait dans l'autre sens, et `flag` mettait en attente
        // de modération une file que personne n'arbitrait.
        //
        // La note d'un produit et celle d'un vendeur se calculent sur les avis
        // publiés : ces trois routes réécrivaient donc la réputation de la place
        // de marché.
        //
        // `MapAdminGroup` inclut `Moderator`, et c'est précisément son objet :
        // arbitrer des contenus n'est pas administrer la plateforme.
        // ═════════════════════════════════════════════════════════════════════
        var moderation = app.MapAdminGroup("/api/engagement/reviews").WithTags("Engagement · Modération");

        // ═════════════════════════════════════════════════════════════════════
        // ET IL MANQUAIT LA FILE, C'EST-À-DIRE CE QUI REND LES TROIS GESTES
        //    UTILISABLES.
        //
        // Les trois routes ci-dessous sont adressées par identifiant d'avis. Rien
        // ne disait QUELS avis attendent : `ListByProductAsync` ne rend que le
        // publié, `ListBySellerAsync` demande un vendeur. Un avis signalé restait
        // donc `Flagged` sans que personne ne le voie — la modération existait
        // sur le papier et pas dans les faits.
        //
        // `/moderation` ET NON `/`. LA RAISON ÉCRITE ICI ÉTAIT FAUSSE, ET LE
        //    CHEMIN RESTE POURTANT LE BON.
        //
        // Ce commentaire affirmait qu'un `MapGet("/")` entrerait en collision
        // avec le groupe authentifié voisin. C'est inexact : celui-ci monte
        // `MapGet("/{id:guid}")` — un gabarit à un segment de plus — et
        // `MapPost("/")` — un autre verbe. Le routage d'ASP.NET Core les
        // distingue tous les deux sans ambiguïté.
        //
        // Le vrai motif est plus simple, et il tient : `GET /api/engagement/reviews`
        // se lirait comme « les avis », alors que cette route rend une FILE
        // D'ARBITRAGE, ordonnée du plus ancien au plus récent et réservée aux
        // modérateurs. Le chemin nomme ce qu'on ouvre.
        //
        // Corrigé le 23/08 : une raison fausse survit plus longtemps qu'un bogue,
        // parce que personne ne la relit.
        // ═════════════════════════════════════════════════════════════════════
        moderation.MapGet("/moderation", ListReviewsForModerationAsync);

        moderation.MapPost("/{id:guid}/flag", FlagReviewAsync);
        moderation.MapPost("/{id:guid}/reject", RejectReviewAsync);
        moderation.MapPost("/{id:guid}/restore", RestoreReviewAsync);

        var recommendations = app.MapAuthenticatedGroup("/api/engagement/recommendations").WithTags("Engagement · Recommendations");
        recommendations.MapGet("/product/{productId:guid}", GetProductRecommendationsAsync);
        recommendations.MapGet("/me", GetMyRecommendationsAsync);
        recommendations.MapGet("/users/{userId:guid}", GetUserRecommendationsAsync);

        // ÉCRIRE UNE RECOMMANDATION, C'EST ÉCRIRE LA PAGE D'ACCUEIL.
        //
        // La route acceptait la commande brute dans le corps : n'importe quel
        // inscrit choisissait les produits mis en avant sur la fiche d'un
        // concurrent, ou dans les suggestions d'un autre utilisateur. Les
        // recommandations sont calculées par la plateforme ; leur écriture est
        // une opération de plateforme.
        var recommendationsAdmin = app.MapAdminGroup("/api/engagement/recommendations")
            .WithTags("Engagement · Recommendations · Admin");

        // ═════════════════════════════════════════════════════════════════════
        // ON ÉCRIVAIT LA PAGE D'ACCUEIL SANS POUVOIR LA RELIRE.
        //
        // L'upsert ci-dessous persiste depuis l'origine ; les trois lectures du
        // groupe authentifié sont toutes ADRESSÉES — par produit, par
        // utilisateur, ou « les miennes ». Aucune ne répond à « qu'est-ce qui
        // est mis en avant en ce moment ». Même situation que les avis avant la
        // file de modération : le geste existait, la vue d'ensemble non.
        //
        // `MapGet("/")` ICI EST SANS AMBIGUÏTÉ. Le groupe authentifié voisin
        // monte `/product/{productId:guid}`, `/me` et `/users/{userId:guid}` :
        // aucun de ces gabarits ne correspond au chemin nu. Contrairement au
        // groupe de modération des avis plus haut, rien n'impose ici un chemin
        // distinct.
        //
        // GROUPE ADMIN ET NON AUTHENTIFIÉ : cette page dit quels produits la
        // plateforme pousse, et sur les fiches de qui. C'est la même donnée que
        // l'écriture protège — la relayer en lecture ouverte annulerait la
        // garde par l'autre bout.
        // ═════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// Réponse publique du vendeur à un avis.
    /// </summary>
    /// <remarks>
    /// LA CORRESPONDANCE UTILISATEUR → VENDEUR APPARTIENT À merchant-service.
    ///
    /// `Review` porte un `SellerId`, le jeton porte un identifiant
    /// d'UTILISATEUR. Le rapprochement des deux a coûté à ce service une
    /// dépendance vers `HBA.Merchants.Contracts` qu'il n'avait pas — c'est le
    /// vrai prix de ce correctif, et non les quelques lignes de contrôle.
    /// </remarks>
    private static async Task<IResult> ReplyToReviewAsync(
        Guid id, ClaimsPrincipal user, BodyRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } callerId
            ? Results.Unauthorized()
            : (await sender.Send(new ReplyToReviewCommand(id, callerId, request.Body), ct))
                .Match(() => Results.NoContent());

    /// <summary>La file de modération des avis (Admin ou Modérateur).</summary>
    /// <remarks>
    /// SANS `status`, ELLE REND TOUT — Y COMPRIS LE PUBLIÉ.
    ///
    /// L'usage courant est `?status=Flagged`. Mais restreindre la route aux seuls
    /// signalés interdirait de relire un avis rejeté pour le restaurer, ce que
    /// `restore` permet précisément. Le filtre est un paramètre, pas une règle.
    /// </remarks>
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

    /// <remarks>
    /// LE JUMEAU DE `/me`, AVEC UN IDENTIFIANT DANS L'URL.
    ///
    /// Il rendait à tout inscrit les suggestions calculées pour un autre compte
    /// — c'est-à-dire ce que la plateforme a déduit de sa navigation et de ses
    /// achats. La route reste utile à l'administration ; elle ne l'est à
    /// personne d'autre que son propre titulaire.
    /// </remarks>
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
        // directe sur `HBA.Shared.Application` pour une seule constante. Un
        // paramètre non nullable rendrait au contraire ce défaut inatteignable
        // depuis HTTP — c'est le piège des routes de portefeuille.
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
