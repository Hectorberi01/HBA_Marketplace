using System.Security.Claims;
using HBA.Merchants.Contracts;

// LA ROUTE DE RELANCE APPELLE LE GESTIONNAIRE DE CRÉATION DE COURSE.
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Application.Orders.Commands.PlaceOrder;
using HBA.Orders.Application.Orders.Queries;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Orders.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Order.</summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // Authentifié SEUL ne suffit jamais : chaque handler ci-dessous prouve que
        // l'appelant est bien l'acheteur de la commande qu'il touche.
        var orders = app.MapAuthenticatedGroup("/api/orders").WithTags("Orders");
        orders.MapGet("/", ListMineAsync);
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapPost("/", PlaceAsync);
        orders.MapPost("/{id:guid}/cancel", CancelAsync);

        // LE PRÉFIXE `/admin` N'A JAMAIS PROTÉGÉ QUOI QUE CE SOIT.
        var admin = app.MapAdminGroup("/api/admin/orders").WithTags("Admin · Orders");
        admin.MapGet("/", ListAllAsync);

        // LA TRAPPE D'EXPLOITATION ANNONCÉE CI-DESSUS. LA VOICI, ET ELLE EST ICI.
        admin.MapPost("/{id:guid}/review/resume", ResumeAfterReviewAsync);
        admin.MapPost("/{id:guid}/review/refund", RefundAfterReviewAsync);

        // Le vendeur ne peut lire ni toucher que SES commandes : les six routes de
        // ce groupe passent par `DenyUnlessOwnSellerAsync`, chacune avec SA
        // capacité.
        // `MapSellerGroup` ET NON `MapAuthenticatedGroup` — ALIGNEMENT DE L'AUDIT.
        //
        // La route portait déjà sa garde, donc rien n'était ouvert. Mais c'est
        // exactement l'état que `MapSellerGroup` existe pour remplacer : « la
        // protection était une discipline, pas une barrière ». Ce groupe et celui
        // d'inventory étaient les deux derniers à ne pas l'être.
        //
        // « C'est ici que viendront les routes de confirmation et de préparation
        // qu'`ORDER_MANAGER` attend » — c'était écrit ici, et c'était la trace la
        // plus visible d'ISSUE-026. Elles sont écrites juste en dessous.
        var seller = app.MapSellerGroup("/api/sellers/{sellerId:guid}/orders").WithTags("Seller · Orders");
        seller.MapGet("/", ListBySellerAsync);

        // LES CINQ ROUTES QU'`ORDER_MANAGER` ATTENDAIT (ISSUE-026).
        seller.MapPost("/{orderId:guid}/confirm", ConfirmSellerOrderAsync);
        seller.MapPost("/{orderId:guid}/reject", RejectSellerOrderAsync);
        seller.MapPost("/{orderId:guid}/preparing", MarkSellerOrderPreparingAsync);
        seller.MapPost("/{orderId:guid}/ready", MarkSellerOrderReadyAsync);
        seller.MapPost("/{orderId:guid}/cancel", CancelSellerOrderAsync);

        return app;
    }

    private static async Task<IResult> ListMineAsync(
        int? take, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyOrdersQuery(buyerId, take ?? 50), ct)).Match(Results.Ok);

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetOrderQuery(id, buyerId), ct)).Match(Results.Ok);

    /// <summary>La file d'arbitrage se lit ici : <c>?status=UnderReview</c>.</summary>
    private static async Task<IResult> ListAllAsync(
        int page, int pageSize, string? search, string? status, string? sort, string? dir, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllOrdersQuery(page, pageSize, search, status, sort, dir), ct)).Match(Results.Ok);

    /// <summary>L'exploitation RELANCE une commande en arbitrage.</summary>
    private static async Task<IResult> ResumeAfterReviewAsync(
        Guid id,
        ISender sender,
        CreateDeliveryOnOrderConfirmedHandler courses,
        ILogger<CreateDeliveryOnOrderConfirmedHandler> logger,
        CancellationToken ct)
    {
        var commande = await sender.Send(new GetOrderQuery(id), ct);
        if (commande.IsFailure)
        {
            return commande.Match(_ => Results.NoContent());
        }

        var reprise = await sender.Send(new ResumeOrderAfterReviewCommand(id), ct);
        if (reprise.IsFailure)
        {
            return reprise.Match(() => Results.NoContent());
        }

        if (string.Equals(commande.Value.Kind, "Food", StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Commande de repas {OrderId} relancée. Aucune course demandée depuis ici : "
                + "food-service la créera quand le sac sera prêt.",
                id);

            return Results.NoContent();
        }

        await courses.DemanderCourseAsync(id, ct);

        // ON RELIT LA COMMANDE AVANT DE RÉPONDRE 204.
        var apres = await sender.Send(new GetOrderQuery(id), ct);

        return apres.Match(o => o.UnderReviewSinceUtc is null
            ? Results.NoContent()
            : Results.Conflict(new { motif = o.ReviewReason }));
    }

    /// <summary>
    /// L'exploitation RETOURNE la vente : la commande est annulée, et
    /// financial-service rembourse en consommant <c> OrderCancelled</c>.
    /// </summary>
    private static async Task<IResult> RefundAfterReviewAsync(
        Guid id, ReasonRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefundOrderAfterReviewCommand(id, request.Reason), ct))
            .Match(() => Results.NoContent());

    /// <summary>
    /// Les commandes d'un vendeur — pour CE vendeur, ou pour l'administration.
    /// </summary>
    /// <remarks>
    /// LA GARDE A ÉTÉ EXTRAITE, PAS ALLÉGÉE : voir
    /// <see cref="DenyUnlessOwnSellerAsync"/>, qui porte l'argumentaire complet —
    /// pourquoi un appel gRPC pour une autorisation, pourquoi `GetAccessAsync` et
    /// non `GetSellerByUserIdAsync`, pourquoi 403 et non 404.
    ///
    /// Elle est désormais partagée avec les cinq routes de transition : cinq
    /// recopies du même préambule auraient fini par diverger, et c'est dans la
    /// copie qu'on oublie le contrôle de capacité.
    ///
    /// CETTE ROUTE RESTE SUR `ORDER_VIEW`. Lire un carnet et confirmer une
    /// commande payée ne sont pas le même geste — c'est tout l'objet du paramètre
    /// de capacité.
    /// </remarks>
    private static async Task<IResult> ListBySellerAsync(
        Guid sellerId,
        int? take,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
    {
        var refus = await DenyUnlessOwnSellerAsync(
            sellerId, user, access, MerchantCapabilities.OrderView, ct);

        return refus ?? (await sender.Send(new ListOrdersBySellerQuery(sellerId, take ?? 50), ct)).Match(Results.Ok);
    }

    /// <summary>
    /// L'appelant a-t-il le droit d'agir sur le carnet de CE vendeur, avec CETTE
    /// capacité ? Rend <c>null</c> si oui, la réponse d'échec sinon.
    /// </summary>
    /// <remarks>
    /// DEUX QUESTIONS, PAS UNE — ET LES CONFONDRE OUVRE UN TROU DE CHAQUE CÔTÉ.
    ///
    /// « Ce vendeur est-il le vôtre ? » et « votre rôle vous autorise-t-il CE
    /// geste-là ? » sont indépendantes. Les fondre sous un contrôle unique
    /// reviendrait à dire qu'un membre autorisé à LIRE le carnet peut aussi
    /// refuser une commande payée — c'est-à-dire annuler une vente. C'est
    /// exactement la confusion que le §10 sépare en six permissions distinctes,
    /// et c'est pourquoi la capacité est un PARAMÈTRE : les six routes de ce
    /// groupe n'exigent pas la même.
    ///
    /// `GetAccessAsync` ET NON `GetSellerByUserIdAsync`.
    ///
    /// La seconde ne résout que les PROPRIÉTAIRES. Le carnet de commandes est
    /// pourtant l'écran de travail du gestionnaire de commandes et du service
    /// client — deux rôles que le §10 crée explicitement, et que cette route
    /// renvoyait en 403 parce qu'ils n'ont pas de dossier vendeur à leur nom.
    ///
    /// POURQUOI UN APPEL gRPC POUR UNE SIMPLE AUTORISATION.
    ///
    /// Le jeton porte un identifiant d'UTILISATEUR ; la route porte un
    /// identifiant de VENDEUR. Les deux ne sont pas le même nombre, et
    /// order-service ne connaît pas la correspondance : elle appartient à
    /// merchant-service. Sans cet appel, la seule chose vérifiable serait
    /// « l'appelant a un compte » — et tout inscrit confirmerait les commandes de
    /// n'importe quel vendeur, concurrents compris.
    ///
    /// Le BFF marchand résout déjà ce `sellerId` avant d'appeler ici. Cela ne
    /// suffit pas : la route est joignable directement à travers la passerelle,
    /// et une vérification faite par l'appelant n'est pas une vérification.
    ///
    /// 403 ICI, PAS 404 : L'EXISTENCE D'UN VENDEUR N'EST PAS UN SECRET.
    ///
    /// Contrairement au catalogue, où le 404 empêche d'énumérer les fiches,
    /// `sellerId` vient de l'URL et les boutiques sont publiques. Cacher
    /// l'existence ne protégerait rien et rendrait le diagnostic impossible au
    /// membre légitime qui s'est trompé d'identifiant. C'est la règle explicite du
    /// dépôt, alignée avec `FinancialEndpoints.DenyUnlessOwnSellerAsync` :
    /// identifiant de VENDEUR venu de l'URL → 403 enveloppé ; identifiant de
    /// RESSOURCE → 404.
    ///
    /// `storeId` NUL, ET CE N'EST PAS UN CONTOURNEMENT.
    ///
    /// Ni `OrderLine` ni `SellerOrder` ne portent de boutique : le panier ne dit
    /// pas de quel point de vente part chaque ligne. `CanInStore(null, …)`
    /// retombe donc sur l'union des droits du membre, ce qui est le comportement
    /// d'avant le cadrage — c'est une limite du SCHÉMA, nommée comme telle dans
    /// `IMerchantAccessApi`, et c'est là qu'il faudra la lever.
    /// </remarks>
    private static async Task<IResult?> DenyUnlessOwnSellerAsync(
        Guid sellerId,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        string capacite,
        CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        // L'administration voit et débloque tous les carnets : c'est elle qui
        // reprend une commande qu'un vendeur a laissée en plan.
        if (user.IsInRole(ApiAuthorization.AdminRole) || user.IsInRole(ApiAuthorization.ModeratorRole))
        {
            return null;
        }

        var acces = await access.GetAccessAsync(userId, ct);

        if (acces is null || acces.SellerId != sellerId)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Ce carnet de commandes n'est pas le vôtre.",
                StatusCodes.Status403Forbidden);
        }

        return acces.Can(capacite) ? null : ApiResults.MissingCapability(capacite);
    }

    /// <summary>Le vendeur s'engage à honorer sa part.</summary>
    private static async Task<IResult> ConfirmSellerOrderAsync(
        Guid sellerId, Guid orderId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderConfirm, ct)
        ?? (await sender.Send(new ConfirmSellerOrderCommand(orderId, sellerId), ct))
            .Match(() => Results.NoContent());

    /// <summary>Le vendeur REFUSE sa part — d'une commande DÉJÀ PAYÉE.</summary>
    private static async Task<IResult> RejectSellerOrderAsync(
        Guid sellerId, Guid orderId, ReasonRequest request, ClaimsPrincipal user,
        IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderReject, ct)
        ?? (await sender.Send(new RejectSellerOrderCommand(orderId, sellerId, request.Reason), ct))
            .Match(() => Results.NoContent());

    /// <summary>Le colis se monte. Permission `ORDER_MARK_PREPARING`.</summary>
    private static async Task<IResult> MarkSellerOrderPreparingAsync(
        Guid sellerId, Guid orderId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderMarkPreparing, ct)
        ?? (await sender.Send(new MarkSellerOrderPreparingCommand(orderId, sellerId), ct))
            .Match(() => Results.NoContent());

    /// <summary>Le colis attend le livreur.</summary>
    private static async Task<IResult> MarkSellerOrderReadyAsync(
        Guid sellerId, Guid orderId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderMarkReady, ct)
        ?? (await sender.Send(new MarkSellerOrderReadyCommand(orderId, sellerId), ct))
            .Match(() => Results.NoContent());

    /// <summary>Le vendeur se dédit APRÈS s'être engagé.</summary>
    private static async Task<IResult> CancelSellerOrderAsync(
        Guid sellerId, Guid orderId, ReasonRequest request, ClaimsPrincipal user,
        IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderCancel, ct)
        ?? (await sender.Send(new CancelSellerOrderCommand(orderId, sellerId, request.Reason), ct))
            .Match(() => Results.NoContent());

    /// <summary>L'acheteur passe commande.</summary>
    private static async Task<IResult> PlaceAsync(ClaimsPrincipal user, PlaceOrderRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new PlaceOrderCommand(
                buyerId,
                request.ShippingAddress,
                request.DeliveryQuoteId), ct))
                .Match(id => Results.Created($"/api/orders/{id}", new { id }));

    private static async Task<IResult> CancelAsync(
        Guid id, ReasonRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new CancelOrderCommand(id, request.Reason, buyerId), ct))
                .Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <param name="DeliveryQuoteId">
    /// L'identifiant du devis de course affiché à l'acheteur — « qt_… ».
    /// </param>
    public sealed record PlaceOrderRequest(
        ShippingAddressInput? ShippingAddress,
        string? DeliveryQuoteId);

    public sealed record ReasonRequest(string Reason);
}
