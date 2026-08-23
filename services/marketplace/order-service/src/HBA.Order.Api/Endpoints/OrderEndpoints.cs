using System.Security.Claims;
using HBA.Merchants.Contracts;

// LA ROUTE DE RELANCE APPELLE LE GESTIONNAIRE DE CRÉATION DE COURSE.
//
// Il vit dans le composition root parce qu'il connaît à la fois la commande, le
// lieu d'expédition et le transporteur. Réécrire son enchaînement ici — devis
// payé d'abord, repli sans devis, refus du multi-lieux — en produirait une
// seconde version qui divergerait au premier correctif.
using HBA.Orders.Api.Integration;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Application.Orders.Commands.PlaceOrder;
using HBA.Orders.Application.Orders.Queries;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Orders.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Order.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI A ÉTÉ RETIRÉ D'ICI, ET POURQUOI IL NE DOIT PAS REVENIR.
///
/// Trois routes exposaient des TRANSITIONS DE SAGA à qui présentait un jeton,
/// sans rôle ni contrôle de propriété :
///
///   POST /{id}/payment/confirm  → MarkPaid, décrément du stock, Confirm, accrual
///                                 des gains, création de la course, escrow libéré
///                                 à la livraison. Autrement dit : CONFIRMER UNE
///                                 COMMANDE SANS JAMAIS L'AVOIR PAYÉE.
///   POST /{id}/delivered        → escrow libéré et vendeur crédité, sur ordre de
///                                 n'importe quel inscrit.
///   POST /{id}/provider/reject  → refus « par le restaurant » prononcé par un tiers.
///
/// Aucune n'avait d'appelant : ni BFF, ni application mobile, ni passerelle. Les
/// trois transitions sont pilotées par Kafka et le restent —
/// `ConfirmOrderOnPaymentCapturedHandler`, `MarkOrderDeliveredOnDeliveryCompletedHandler`,
/// `CancelOrderOnFoodOrderRefusedHandlers`. C'était de la surface d'attaque pure.
///
/// RÈGLE : une étape de saga ne s'expose pas en HTTP. Si un jour il faut une
/// trappe d'exploitation pour débloquer une commande à la main, elle passe par
/// `MapAdminGroup`, elle porte un motif, et elle se journalise — elle ne se
/// glisse pas dans le groupe de l'acheteur.
///
/// CE JOUR EST ARRIVÉ, ET LA RÈGLE A ÉTÉ TENUE.
///
/// Les deux routes d'ARBITRAGE — relancer, rembourser — sont exactement cette
/// trappe : une commande payée peut devenir inexécutable (course annulée,
/// expédition multi-lieux) et il faut bien qu'un humain la débloque. Elles sont
/// dans `MapAdminGroup`, le remboursement porte un motif écrit dans la commande,
/// et rien n'en a été glissé dans le groupe de l'acheteur — qui, lui, y verrait
/// de quoi relancer une course à volonté ou déclencher son propre remboursement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // Authentifié SEUL ne suffit jamais : chaque handler ci-dessous prouve
        // que l'appelant est bien l'acheteur de la commande qu'il touche.
        var orders = app.MapAuthenticatedGroup("/api/orders").WithTags("Orders");
        orders.MapGet("/", ListMineAsync);
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapPost("/", PlaceAsync);
        orders.MapPost("/{id:guid}/cancel", CancelAsync);

        // LE PRÉFIXE `/admin` N'A JAMAIS PROTÉGÉ QUOI QUE CE SOIT.
        //
        // Ce groupe s'appelait « Admin · Orders » et rendait, à tout compte
        // inscrit, la liste paginée de TOUTES les commandes de la plateforme —
        // acheteurs, adresses, montants. Le nom disait admin, la politique
        // disait authentifié.
        var admin = app.MapAdminGroup("/api/admin/orders").WithTags("Admin · Orders");
        admin.MapGet("/", ListAllAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LA TRAPPE D'EXPLOITATION ANNONCÉE CI-DESSUS. LA VOICI, ET ELLE EST ICI.
        //
        // SANS ELLE, L'ÉTAT « EN ARBITRAGE » SERAIT UNE IMPASSE DE PLUS.
        //
        // Une commande devenue inexécutable y entre — course annulée, expédition
        // multi-lieux — et il faut bien que quelqu'un l'en sorte. Sans ces deux
        // routes, on aurait remplacé une commande bloquée en « Confirmed » par
        // une commande bloquée en « UnderReview » : même argent gelé, même stock
        // décrémenté, même acheteur qui attend.
        //
        // DEUX ISSUES, ET LE CHOIX APPARTIENT À UN HUMAIN.
        //
        // Une course annulée est le plus souvent RÉATTRIBUABLE. Rembourser
        // d'office détruirait des ventes récupérables, et l'argent rendu ne se
        // reprend pas — d'où deux routes distinctes plutôt qu'un automatisme :
        //
        //   resume → la commande repart, une nouvelle course est demandée ;
        //   refund → la vente est retournée, financial rembourse en consommant
        //            `OrderCancelled`.
        //
        // DANS `MapAdminGroup`, JAMAIS DANS LE GROUPE DE L'ACHETEUR.
        //
        // Ce sont des transitions de saga. Ouvertes à l'acheteur, elles lui
        // donneraient de quoi relancer une course à volonté, ou déclencher son
        // propre remboursement sur une vente conclue — exactement les trois
        // routes retirées d'ici, et pour la même raison.
        //
        // Le motif est OBLIGATOIRE sur le remboursement : il est écrit dans la
        // commande, et c'est la seule trace de qui a décidé quoi.
        // ═════════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // LES CINQ ROUTES QU'`ORDER_MANAGER` ATTENDAIT (ISSUE-026). LES VOICI.
        //
        // CINQ PERMISSIONS EXISTAIENT, ÉTAIENT DISTRIBUÉES, ET NE GARDAIENT
        // RIEN.
        //
        // `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`,
        // `ORDER_MARK_READY` et `ORDER_CANCEL` étaient déclarées dans
        // `MerchantPermissions`, attribuées au rôle `ORDER_MANAGER`, affichées
        // dans la console d'équipe — et aucune route du dépôt ne les exigeait. Le
        // rôle promettait une autorité qu'il n'exerçait pas, et le parcours
        // vendeur s'arrêtait à la RÉCEPTION de la commande : il n'y avait
        // strictement rien à faire changer d'état.
        //
        // CE N'EST PAS UNE ENTORSE À LA RÈGLE DU HAUT DE CE FICHIER.
        //
        // « Une étape de saga ne s'expose pas en HTTP » : les trois routes
        // retirées d'ici faisaient prononcer par n'importe quel inscrit des
        // transitions de la commande — paiement confirmé, colis livré, refus du
        // restaurant. Celles-ci ne touchent PAS la commande. Elles font changer
        // d'état la part d'UN vendeur, elles sont prononcées par la seule partie
        // qui connaît le fait (« j'accepte », « j'emballe », « c'est prêt »), et
        // chacune porte sa permission ET une garde d'appartenance.
        //
        // La distinction est celle-là, et il faut la tenir : `ORDER_MARK_READY`
        // existe parce que le vendeur SAIT que le colis est prêt ; il n'existe pas
        // de `ORDER_MARK_HANDED_OVER`, parce que la remise au livreur est
        // constatée par le livreur — la laisser déclarer par le vendeur
        // rouvrirait, à l'échelle de la part, exactement ce qui a été retiré à
        // l'échelle de la commande.
        //
        // ADRESSÉES PAR (VENDEUR, COMMANDE), PAS PAR `SellerOrderId`.
        //
        // Le vendeur ne connaît pas cet identifiant : son carnet est une liste de
        // COMMANDES, et c'est ce que `ListBySellerAsync` lui rend. Le couple est
        // aussi ce qui rend la commande applicative intrinsèquement cadrée — même
        // appelée depuis un chemin qui aurait oublié sa garde, elle ne peut
        // toucher que la part de ce vendeur-là.
        //
        // LE MOTIF EST OBLIGATOIRE SUR LES DEUX REFUS, ET C'EST LE POINT.
        //
        // C'est la seule trace de pourquoi une commande PAYÉE ne sera pas
        // honorée, et elle sera relue le jour où le client réclame. L'agrégat
        // refuse un motif vide (400) : la route ne peut donc pas l'oublier.
        // ═════════════════════════════════════════════════════════════════════
        seller.MapPost("/{orderId:guid}/confirm", ConfirmSellerOrderAsync);
        seller.MapPost("/{orderId:guid}/reject", RejectSellerOrderAsync);
        seller.MapPost("/{orderId:guid}/preparing", MarkSellerOrderPreparingAsync);
        seller.MapPost("/{orderId:guid}/ready", MarkSellerOrderReadyAsync);
        seller.MapPost("/{orderId:guid}/cancel", CancelSellerOrderAsync);

        return app;
    }

    /// <remarks>
    /// `take` EST UN SOUHAIT, PAS UN ORDRE — plafonné par le handler.
    ///
    /// Cet historique n'avait AUCUNE borne (§12) : il remontait entier, avec les
    /// lignes et les options de chaque commande. Un acheteur fidèle payait sa
    /// fidélité à chaque ouverture de « mes commandes ». La vraie réponse est une
    /// pagination de cette route ; elle change le contrat, donc elle se décide avec
    /// les clients web et mobile.
    /// </remarks>
    private static async Task<IResult> ListMineAsync(
        int? take, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyOrdersQuery(buyerId, take ?? 50), ct)).Match(Results.Ok);

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetOrderQuery(id, buyerId), ct)).Match(Results.Ok);

    /// <summary>
    /// La file d'arbitrage se lit ici : <c>?status=UnderReview</c>.
    ///
    /// AUCUN FILTRE À AJOUTER — `ListAllOrdersQuery` fait déjà un
    /// `Enum.TryParse` sur `OrderStatus`, et le dépôt compte les commandes par
    /// statut. La valeur ajoutée à l'énumération devient donc filtrable et
    /// comptabilisée sans une ligne de plus, motif et ancienneté compris (voir
    /// `OrderSummary.ReviewReason`).
    /// </summary>
    private static async Task<IResult> ListAllAsync(
        int page, int pageSize, string? search, string? status, string? sort, string? dir, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllOrdersQuery(page, pageSize, search, status, sort, dir), ct)).Match(Results.Ok);

    /// <summary>
    /// L'exploitation RELANCE une commande en arbitrage.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX GESTES, ET LE SECOND EST TOUT L'INTÉRÊT DU PREMIER.
    ///
    /// Sortir la commande de l'arbitrage ne la fait pas livrer : sans nouvelle
    /// course, elle redeviendrait « confirmée » et s'y rebloquerait aussitôt —
    /// on aurait déplacé l'impasse d'un état à l'autre.
    ///
    /// L'ÉCHEC DE LA COURSE N'ANNULE PAS LA REPRISE.
    ///
    /// La cause du blocage est parfois toujours là — deux lieux d'expédition
    /// qu'on n'a pas regroupés. `DemanderCourseAsync` remet alors la commande en
    /// arbitrage de lui-même, avec un motif à jour : l'exploitation voit que sa
    /// relance n'a rien changé, plutôt que de recevoir une erreur muette.
    ///
    /// ON NE RELANCE PAS UNE COMMANDE DE REPAS PAR ICI.
    ///
    /// Sa course est créée par food-service quand le SAC EST PRÊT. En poser une
    /// depuis order-service enverrait un livreur chercher un plat qui n'a pas
    /// encore été cuisiné, et sous une référence `ORDER-` que le ticket de
    /// cuisine ne reconnaîtrait jamais.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
        //
        // `DemanderCourseAsync` ne lève pas quand la cause du blocage est
        // toujours là — deux lieux d'expédition qu'on n'a pas regroupés : elle
        // remet la commande en arbitrage et rend la main normalement. Répondre
        // « c'est fait » à un exploitant dont la relance n'a rien changé serait
        // exactement le silence que tout ce chemin existe pour rompre. Il voit
        // donc un 409 qui porte le motif à jour.
        //
        // Une panne de delivery-service, elle, remonte en 500 : c'est passager,
        // et l'exploitant doit simplement recommencer.
        var apres = await sender.Send(new GetOrderQuery(id), ct);

        return apres.Match(o => o.UnderReviewSinceUtc is null
            ? Results.NoContent()
            : Results.Conflict(new { motif = o.ReviewReason }));
    }

    /// <summary>
    /// L'exploitation RETOURNE la vente : la commande est annulée, et
    /// financial-service rembourse en consommant <c>OrderCancelled</c>.
    /// </summary>
    /// <remarks>
    /// LE MOTIF EST ÉCRIT DANS LA COMMANDE, PAS SEULEMENT DANS UN JOURNAL.
    ///
    /// C'est la seule trace de la décision : elle rend de l'argent, elle est
    /// irréversible, et elle sera relue le jour où le client réclame.
    /// </remarks>
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
    /// ═════════════════════════════════════════════════════════════════════════
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
    /// ═════════════════════════════════════════════════════════════════════════
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

    /// <summary>Le vendeur s'engage à honorer sa part. Permission `ORDER_CONFIRM`.</summary>
    private static async Task<IResult> ConfirmSellerOrderAsync(
        Guid sellerId, Guid orderId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderConfirm, ct)
        ?? (await sender.Send(new ConfirmSellerOrderCommand(orderId, sellerId), ct))
            .Match(() => Results.NoContent());

    /// <summary>
    /// Le vendeur REFUSE sa part — d'une commande DÉJÀ PAYÉE.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE REFUS NE REMBOURSE PERSONNE AUJOURD'HUI.
    ///
    /// Le client a payé. Cette route change l'état de la part et publie
    /// `SellerOrderRefusedIntegrationEvent` — qui n'a AUCUN consommateur. Ni
    /// stock rendu, ni remboursement de la part, ni notification : les trois
    /// gestes vivent dans inventory-service, financial-service et
    /// communication-service, hors du périmètre de ce lot, et le plus lourd des
    /// trois — rembourser une FRACTION de commande — n'existe pas encore comme
    /// capacité.
    ///
    /// C'est écrit ici parce que c'est ici qu'on ouvre le geste. Une lacune
    /// nommée vaut mieux qu'un silence.
    ///
    /// LE MOTIF EST OBLIGATOIRE, ET AUCUN DÉFAUT NE LE REMPLACE.
    ///
    /// C'est la seule trace de pourquoi cette commande ne sera pas honorée.
    /// Inventer « refusée par le vendeur » à sa place fabriquerait une
    /// justification qu'il n'a pas donnée — et c'est ce texte-là qu'on relira le
    /// jour où le client réclame. L'agrégat rend un 400 sur un motif vide.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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

    /// <summary>
    /// Le colis attend le livreur. Permission `ORDER_MARK_READY`.
    /// </summary>
    /// <remarks>
    /// CET ÉTAT NE DÉCLENCHE PAS ENCORE L'ENLÈVEMENT.
    ///
    /// La course d'une commande de marchandise est créée à la CONFIRMATION de la
    /// commande (`CreateDeliveryOnOrderConfirmedHandler`), pas quand le colis est
    /// prêt — c'est la restauration qui attend le sac. Déclarer « prête » informe
    /// donc l'acheteur et l'exploitation ; cela ne dépêche personne. Brancher
    /// l'enlèvement sur cet état demanderait de déplacer la création de course
    /// pour toute la marketplace, ce qui n'est pas un effet de bord de ce lot.
    /// </remarks>
    private static async Task<IResult> MarkSellerOrderReadyAsync(
        Guid sellerId, Guid orderId, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderMarkReady, ct)
        ?? (await sender.Send(new MarkSellerOrderReadyCommand(orderId, sellerId), ct))
            .Match(() => Results.NoContent());

    /// <summary>
    /// Le vendeur se dédit APRÈS s'être engagé. Permission `ORDER_CANCEL`.
    /// </summary>
    /// <remarks>
    /// LA SEULE DES CINQ CLASSÉE SENSIBLE dans `MerchantPermissions`, et c'est
    /// justifié : se dédire après avoir fait attendre le client n'est pas le même
    /// geste que refuser tout de suite. Même conséquence en aval, cependant — et
    /// la même lacune : voir `RejectSellerOrderAsync`, rien n'est encore
    /// remboursé.
    ///
    /// ELLE N'ANNULE PAS LA COMMANDE. Elle ferme la part de CE vendeur. Une
    /// commande à deux vendeurs dont un se dédit reste vivante pour l'autre — et
    /// c'est très exactement ce que l'agrégat existe pour rendre exprimable.
    /// </remarks>
    private static async Task<IResult> CancelSellerOrderAsync(
        Guid sellerId, Guid orderId, ReasonRequest request, ClaimsPrincipal user,
        IMerchantAccessApi access, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, MerchantCapabilities.OrderCancel, ct)
        ?? (await sender.Send(new CancelSellerOrderCommand(orderId, sellerId, request.Reason), ct))
            .Match(() => Results.NoContent());

    /// <summary>
    /// L'acheteur passe commande.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `ShippingFee` A ÉTÉ RETIRÉ DU CORPS, ET IL NE DOIT PAS Y REVENIR.
    ///
    /// Ce champ existait, et il était recopié tel quel dans la commande puis dans
    /// le total encaissé. Un acheteur postait `{ "shippingFee": 0 }` et se faisait
    /// livrer gratuitement : la course était pourtant bel et bien achetée à
    /// delivery-service au prix réel, et la plateforme réglait deux mille francs
    /// sur une commande qui en avait encaissé zéro. Une fois par commande, sans
    /// qu'aucune alerte ne se déclenche — le montant était « celui qu'on avait
    /// demandé ».
    ///
    /// UN CONTRÔLE AURAIT SUFFI ; LA SUPPRESSION EST PLUS SÛRE.
    ///
    /// Vérifier « le montant envoyé correspond bien au devis » aurait laissé le
    /// champ en place, donc la possibilité de l'oublier au prochain appelant, à la
    /// prochaine surface, au prochain BFF. Sans champ, il n'y a plus rien à
    /// falsifier ni à re-vérifier.
    ///
    /// `DeliveryQuoteId`, LUI, RESTE — ET C'EST VOULU.
    ///
    /// Il désigne le prix qu'on a AFFICHÉ à l'acheteur, et c'est celui-là qu'il
    /// doit payer. `PlaceOrderCommandHandler` relit ce devis auprès de
    /// delivery-service et emploie SON montant. Un identifiant n'est pas un
    /// montant : il désigne un montant que seul le serveur peut lire.
    ///
    /// LE MÉCANISME DE DEVIS OPPOSABLE EXISTAIT DEPUIS LE DÉBUT.
    ///
    /// Persisté, horodaté, à usage unique. L'identifiant traversait déjà tout le
    /// circuit jusqu'à la création de course — ce qui donnait au dispositif toutes
    /// les apparences d'être branché. Personne ne relisait le MONTANT.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
    ///
    /// AUCUN MONTANT NE FIGURE ICI, ET C'EST LA CORRECTION.
    ///
    /// Un `decimal ShippingFee` accompagnait ce champ et était encaissé tel quel.
    /// L'acheteur postait zéro et se faisait livrer gratuitement pendant que la
    /// plateforme achetait la course. Le serveur relit désormais le devis désigné
    /// et emploie SON montant : il n'y a plus de nombre à croire.
    /// </param>
    public sealed record PlaceOrderRequest(
        ShippingAddressInput? ShippingAddress,
        string? DeliveryQuoteId);

    public sealed record ReasonRequest(string Reason);
}
