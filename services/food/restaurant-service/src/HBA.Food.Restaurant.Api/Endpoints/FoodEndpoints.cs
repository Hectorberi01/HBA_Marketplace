using System.Security.Claims;
using HBA.Food.Application.Menus;
using HBA.Food.Application.Orders;
using HBA.Food.Application.Restaurants;
using HBA.Food.Contracts;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Staff;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Food.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Food.</summary>
public static class FoodEndpoints
{
    public static IEndpointRouteBuilder MapFoodEndpoints(this IEndpointRouteBuilder app)
    {
        var publicFood = app.MapGroup("/api/food").WithTags("Food · Public");

        // CES DEUX ROUTES SONT LE PRÉALABLE À TOUTE APPLICATION CLIENTE.
        //
        // Avant elles, le service ne savait rendre qu'une carte dont on
        // connaissait DÉJÀ l'identifiant, ou la file de validation. Aucune
        // première page n'était constructible : le BFF client HBA Food n'avait
        // littéralement aucun amont.
        //
        // Anonymes, comme la carte : une vitrine que l'on ne peut pas parcourir
        // sans compte ne convertit personne.
        publicFood.MapGet("/restaurants", ListStorefrontAsync).AllowAnonymous();
        publicFood.MapGet("/restaurants/{id:guid}", GetPublicRestaurantAsync).AllowAnonymous();
        publicFood.MapGet("/restaurants/{id:guid}/menu", GetPublicMenuAsync).AllowAnonymous();

        var partner = app.MapAuthenticatedGroup("/api/food/partner").WithTags("Food · Partner");
        // PRÉALABLE AU SÉLECTEUR D'ACTIVITÉ DE HBA PARTNER.
        //
        // Rien n'exposait « dans quel établissement ce compte travaille-t-il ? ».
        // La fiche publique ne convient pas : elle refuse un dossier en brouillon,
        // c'est-à-dire celui qu'un nouveau restaurateur doit justement voir.
        partner.MapGet("/me", MyRestaurantAsync).WithName("GetMyRestaurant");
        partner.MapPost("/restaurants", RegisterRestaurantAsync);
        partner.MapPut("/restaurants/{id:guid}", UpdateRestaurantAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LES TROIS ROUTES QUI MANQUAIENT ENTRE « CRÉER » ET « SOUMETTRE ».
        //
        // `Restaurant.SubmitForApproval` exige TROIS choses, et aucune n'était
        // atteignable par HTTP :
        //
        //   • des heures de service — sans elles, `CanAcceptOrders` refuserait
        //     toujours, et le maquis validé n'encaisserait jamais rien ;
        //   • un lieu de collecte — sans lui, aucune course ne peut être bâtie,
        //     et le repas prêt attend un livreur que personne n'a demandé ;
        //   • un dossier vendeur de reversement — sans lui, la vente est
        //     encaissée par la plateforme et s'arrête là.
        //
        // Les trois commandes applicatives existaient depuis l'origine
        // (`SetServiceHoursCommand`, `AttachRestaurantLocationCommand`,
        // `AttachRestaurantPayoutSellerCommand`) et n'avaient AUCUN appelant.
        // Conséquence exacte : `POST /restaurants/{id}/submit` répondait 409 à
        // TOUT établissement, quel qu'il soit. Le volet Food n'a donc jamais pu
        // mettre un seul restaurant en service par l'API — et comme la file de
        // validation restait vide, rien ne le signalait côté exploitation.
        //
        // PUT ET NON POST : les trois sont IDEMPOTENTES. Réenvoyer la même
        // grille horaire ou le même dossier ne doit pas empiler quoi que ce
        // soit — c'est ce que fait déjà `SetServiceHours`, qui REMPLACE.
        // ═════════════════════════════════════════════════════════════════════
        partner.MapPut("/restaurants/{id:guid}/service-hours", SetServiceHoursAsync);
        partner.MapPut("/restaurants/{id:guid}/logo", SetRestaurantLogoAsync);
        partner.MapPut("/restaurants/{id:guid}/payout-seller", AttachPayoutSellerAsync);
        partner.MapPut("/restaurants/{id:guid}/location", AttachRestaurantLocationAsync);

        partner.MapPost("/restaurants/{id:guid}/submit", SubmitRestaurantAsync);
        partner.MapGet("/restaurants/{id:guid}/menu", GetOwnerMenuAsync);
        // ═════════════════════════════════════════════════════════════════════
        // LA FILE D'ACCEPTATION — LES DEUX ROUTES QUI MANQUAIENT (tâche #227).
        //
        // `ListPendingFoodOrdersQuery` ET `GetFoodOrderQuery` EXISTAIENT, AVEC
        //    LEUR GESTIONNAIRE ET LEUR PROJECTION, ET AUCUN APPELANT.
        //
        // Le commentaire de `ListPendingFoodOrdersQuery` cite même la route du
        // cahier — « §18 : GET /restaurants/{id}/orders » — et prévient que sans
        // elle « l'acceptation serait un bouton sans liste ». C'est exactement ce
        // qui s'est produit : `accept` et `reject` sont exposées depuis VEN5-a, et
        // le restaurateur n'avait aucun moyen de voir CE qu'il devait accepter.
        //
        // L'ÉCRAN DE CUISINE NE POUVAIT PAS COMBLER CE TROU, et c'est délibéré :
        // `GetKitchenBoardQuery` ÉCARTE explicitement `PendingRestaurantAcceptance`
        // — afficher un ticket non accepté ferait commencer des plats que le
        // restaurant n'a pas encore acceptés. Les deux listes sont distinctes par
        // nature : l'une décide, l'autre exécute.
        //
        // PERMISSION `OrderAccept`, PAS `KitchenManage`. C'est celle
        // qu'exigent `accept` et `reject` : qui peut voir la file doit pouvoir
        // trancher, et l'inverse — voir sans pouvoir agir — n'aurait aucun usage.
        // Le grillardin, lui, a `KitchenManage` et n'a rien à faire ici.
        // ═════════════════════════════════════════════════════════════════════
        partner.MapGet("/restaurants/{id:guid}/orders", ListPendingOrdersAsync);
        partner.MapGet("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}", GetOrderAsync);

        partner.MapGet("/restaurants/{id:guid}/kitchen", KitchenAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/accept", AcceptOrderAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/reject", RejectOrderAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/preparing", StartPreparationAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/ready", MarkReadyAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LA CARTE — ÉCRITE DEPUIS DES MOIS, JAMAIS EXPOSÉE.
        //
        // food-service comptait treize routes HTTP pour soixante-dix commandes
        // applicatives. Menus, catégories, plats, options : tout le domaine
        // existait, avec ses invariants et ses tests, et AUCUN moyen de s'en
        // servir. Un restaurateur validé se connectait à une carte vide qu'il ne
        // pouvait pas remplir.
        //
        // Ce qui suit est le strict nécessaire pour composer une carte. Les
        // suppléments, les créneaux de service et les postes de préparation
        // attendent encore.
        // ═════════════════════════════════════════════════════════════════════
        partner.MapPost("/restaurants/{restaurantId:guid}/menus", CreateMenuAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/menus/{menuId:guid}/categories", CreateCategoryAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/categories/{categoryId:guid}/items", CreateMenuItemAsync);
        partner.MapPut("/restaurants/{restaurantId:guid}/items/{itemId:guid}/price", ChangeItemPriceAsync);

        // LA PHOTO D'UN ARTICLE N'AVAIT AUCUNE ROUTE, alors que tout le reste
        // existait : `MenuItem.ImageMediaId`, `MenuItem.SetImage`,
        // `SetMenuItemImageCommand` et son gestionnaire. Septième cas de la
        // session — la couche applicative écrite, l'endpoint absent.
        //
        // UN `mediaId`, JAMAIS UNE URL. Le fichier est déposé sur media-service,
        // qui rend un identifiant ; food-service n'en connaît que la référence.
        // Accepter une URL laisserait pointer vers n'importe quel domaine — et une
        // photo de plat servie depuis un serveur tiers disparaît le jour où celui-ci
        // ferme, sans que personne ne puisse la retrouver.
        partner.MapPut("/restaurants/{restaurantId:guid}/items/{itemId:guid}/image", SetItemImageAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/items/{itemId:guid}/option-groups", AddOptionGroupAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/items/{itemId:guid}/option-groups/{groupId:guid}/options", AddOptionAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LA CARTE ÉTAIT EN CRÉATION SEULE.
        //
        // Les six routes ci-dessus savent AJOUTER une carte, une section, un
        // plat, et changer un prix. Rien d'autre. Vingt et une des vingt-sept
        // commandes de `MenuCommands.cs` n'avaient aucune route : renommer,
        // masquer, réordonner, supprimer, et surtout MARQUER UN PLAT ÉPUISÉ.
        //
        // « ÉPUISÉ » N'EST PAS UNE COMMODITÉ, C'EST LE GESTE LE PLUS FRÉQUENT
        //    DU MÉTIER.
        //
        // Un maquis tombe en rupture de poisson à 13 h. Sans cette route, ses
        // seules issues étaient de laisser les clients commander un plat qu'il
        // n'a plus — puis refuser les commandes une par une, ce qui déclenche
        // autant de remboursements et abîme sa note — ou d'appeler un
        // administrateur. Le domaine sait le faire depuis des mois.
        //
        // TROIS ÉTATS, UNE SEULE ROUTE. `available`, `sold_out_today`,
        // `unavailable` sont les trois positions d'un même interrupteur côté
        // écran ; trois routes obligeraient l'application à savoir de quel état
        // elle part pour choisir laquelle appeler.
        //
        // CE QUI N'EST DÉLIBÉRÉMENT PAS OUVERT ICI : déplacer une section ou
        // un plat vers une autre carte (`MoveCategory`, `MoveMenuItem`), les
        // créneaux de service (`SetMenuWindow`), la photo d'un plat
        // (`SetMenuItemImage`), et le retrait d'options. Les commandes existent
        // et sont testées ; aucun écran de l'application ne les demande
        // aujourd'hui, et une route sans appelant est une surface d'attaque
        // qu'on entretient pour rien.
        // ═════════════════════════════════════════════════════════════════════
        partner.MapPut("/restaurants/{restaurantId:guid}/menus/{menuId:guid}", RenameMenuAsync);
        partner.MapPut("/restaurants/{restaurantId:guid}/menus/{menuId:guid}/visibility", SetMenuVisibilityAsync);
        partner.MapDelete("/restaurants/{restaurantId:guid}/menus/{menuId:guid}", DeleteMenuAsync);

        partner.MapPut("/restaurants/{restaurantId:guid}/categories/{categoryId:guid}", RenameCategoryAsync);
        partner.MapPut("/restaurants/{restaurantId:guid}/categories/{categoryId:guid}/visibility", SetCategoryVisibilityAsync);
        partner.MapPut("/restaurants/{restaurantId:guid}/categories/{categoryId:guid}/position", ReorderCategoryAsync);
        partner.MapDelete("/restaurants/{restaurantId:guid}/categories/{categoryId:guid}", DeleteCategoryAsync);

        partner.MapPut("/restaurants/{restaurantId:guid}/items/{itemId:guid}", UpdateMenuItemAsync);
        partner.MapPut("/restaurants/{restaurantId:guid}/items/{itemId:guid}/availability", SetItemAvailabilityAsync);
        partner.MapDelete("/restaurants/{restaurantId:guid}/items/{itemId:guid}", DeleteMenuItemAsync);

        // ═════════════════════════════════════════════════════════════════════
        // FERMER LA CUISINE UN MOMENT — SANS FERMER BOUTIQUE.
        //
        // `PauseRestaurantCommand` et `ResumeRestaurantCommand` étaient écrites,
        // testées, et sans amont. Le bouton « Fermer temporairement » du tableau
        // de bord restaurateur ouvrait un écran « bientôt disponible ».
        //
        // LA PAUSE EST BORNÉE DANS LE TEMPS, ET C'EST LE DOMAINE QUI L'EXIGE.
        //
        // Une pause sans échéance qu'on oublie de lever, c'est un établissement
        // qui disparaît de la vitrine pour la soirée sans que personne le
        // remarque. Un coup de feu ou une panne de gaz durent un nombre de
        // minutes connu ; on le demande, et le service reprend seul.
        //
        // `SettingsManage` ET NON `KitchenManage` : suspendre la prise de
        // commande est une décision commerciale, pas un geste de cuisine.
        // ═════════════════════════════════════════════════════════════════════
        partner.MapPost("/restaurants/{id:guid}/pause", PauseRestaurantAsync);
        partner.MapPost("/restaurants/{id:guid}/resume", ResumeRestaurantAsync);

        // ═════════════════════════════════════════════════════════════════════
        // MODÉRATION — GROUPE ADMIN, ET C'EST UNE CORRECTION.
        //
        // `restaurants/pending` ÉTAIT DANS LE GROUPE PARTENAIRE.
        //
        // N'importe quel compte authentifié pouvait donc lister TOUS les dossiers
        // en attente de validation : les noms des établissements concurrents, et
        // le fait qu'ils cherchent à ouvrir. Ce n'est pas une donnée de
        // partenaire, c'est une file de modération. Elle rejoint le groupe admin.
        //
        // ET SURTOUT : LA FILE EXISTAIT SANS AUCUNE ACTION POUR LA VIDER.
        //
        // `ApproveRestaurantCommand` et `RejectRestaurantCommand` sont écrites
        // depuis l'origine. Sans route, aucun restaurant ne pouvait être validé —
        // donc aucun n'entrait en service, donc la vitrine HBA Food restait
        // vide quoi qu'on y fasse. C'est aussi ce qui empêchait l'attribution du
        // rôle FoodPartner, qui suit l'approbation.
        // ═════════════════════════════════════════════════════════════════════
        var moderation = app.MapAdminGroup("/api/food/admin").WithTags("Food · Modération");

        moderation.MapGet("/restaurants/pending", ListPendingRestaurantsAsync);
        moderation.MapPost("/restaurants/{id:guid}/approve", ApproveRestaurantAsync);
        moderation.MapPost("/restaurants/{id:guid}/reject", RejectRestaurantAsync);
        moderation.MapPost("/restaurants/{id:guid}/suspend", SuspendRestaurantAsync);
        moderation.MapPost("/restaurants/{id:guid}/lift-suspension", LiftRestaurantSuspensionAsync);

        return app;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LE CONTRÔLE D'APPARTENANCE QUI MANQUAIT.
    //
    // `UpdateRestaurantAsync` prenait l'identifiant dans l'URL et n'a JAMAIS
    // vérifié que l'appelant travaillait dans cet établissement : tout compte
    // authentifié pouvait renommer n'importe quel restaurant, changer sa
    // description et son téléphone. Les nouvelles routes de carte auraient
    // hérité du même défaut — écrire le plat du voisin, ou son prix.
    //
    // La vérification passe par l'appartenance au personnel ET la permission du
    // §8. Être restaurateur quelque part ne donne aucun droit ailleurs, et un
    // cuisinier ne compose pas la carte.
    //
    // 404 et non 403 : un 403 confirmerait que l'établissement existe.
    // ═════════════════════════════════════════════════════════════════════════
    private static async Task<IResult?> DenyUnlessStaffAsync(
        ClaimsPrincipal user,
        Guid restaurantId,
        FoodPermission permission,
        IFoodModuleApi food,
        CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var membership = await food.GetStaffMembershipAsync(userId, ct);

        if (membership is null || !membership.IsActive || membership.RestaurantId != restaurantId)
        {
            return Results.NotFound();
        }

        return membership.Permissions.Contains(permission.ToCode(), StringComparer.Ordinal)
            ? null
            : Results.Forbid();
    }

    /// <summary>
    /// La vitrine, paginée.
    /// </summary>
    /// <remarks>
    /// `page` ET `pageSize` SONT NULLABLES ET BORNÉS PLUS BAS.
    ///
    /// Les déclarer non nullables obligerait le client à toujours les fournir —
    /// un simple `GET /restaurants` rendrait 400. Les valeurs hors bornes ne sont
    /// pas refusées mais ramenées : refuser `pageSize=1000` par une erreur ne
    /// protège de rien de plus et casse un client qui aurait mal calculé.
    /// </remarks>
    private static async Task<IResult> ListStorefrontAsync(
        int? page, int? pageSize, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListStorefrontQuery(page ?? 1, pageSize ?? 20), ct))
            .Match(cartes => Results.Ok(cartes));

    /// <summary>
    /// La fiche publique d'un établissement.
    /// </summary>
    /// <remarks>
    /// Rend 404 pour un établissement qui existe mais n'est pas en vitrine : cf.
    /// <c>GetPublicRestaurantQuery</c>. Un 403 confirmerait à qui essaie que
    /// l'identifiant correspond à quelque chose.
    /// </remarks>
    private static async Task<IResult> GetPublicRestaurantAsync(
        Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPublicRestaurantQuery(id), ct))
            .Match(restaurant => Results.Ok(restaurant));

    /// <summary>
    /// L'établissement du compte connecté, et son rôle dedans.
    /// </summary>
    /// <remarks>
    /// Rend 404 quand le compte ne travaille nulle part — cas d'un vendeur qui
    /// n'a que des boutiques. L'appelant l'interprète comme « aucune activité
    /// Food », pas comme une erreur.
    /// </remarks>
    private static async Task<IResult> MyRestaurantAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        return (await sender.Send(new GetMyRestaurantQuery(userId), ct))
            .Match(view => Results.Ok(view));
    }

    private static async Task<IResult> GetPublicMenuAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetMenuQuery(id, MenuAudience.Public), ct)).Match(menu => Results.Ok(menu));

    private static async Task<IResult> ListPendingRestaurantsAsync(int? take, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingRestaurantsQuery(take ?? 100), ct)).Match(items => Results.Ok(items));

    /// <remarks>
    /// VUE `Owner` : ELLE MONTRE CE QUE LA VITRINE CACHE.
    ///
    /// Plats désactivés, catégories masquées, articles épuisés du jour, prix non
    /// encore publiés. Sans garde, il suffisait d'un identifiant de restaurant —
    /// public, rendu par la vitrine — pour lire la carte complète d'un
    /// concurrent, brouillons compris. La version publique reste en accès libre,
    /// c'est son rôle.
    /// </remarks>
    private static async Task<IResult> GetOwnerMenuAsync(
        Guid id, ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new GetMenuQuery(id, MenuAudience.Owner), ct))
                .Match(menu => Results.Ok(menu));

    private static async Task<IResult> RegisterRestaurantAsync(
        ClaimsPrincipal user, RegisterRestaurantRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RegisterRestaurantCommand(userId, request.Name, request.Phone), ct))
                .Match(id => Results.Created($"/api/food/restaurants/{id}", new { id }));

    private static async Task<IResult> UpdateRestaurantAsync(
        Guid id, UpdateRestaurantRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct)
            ?? (await sender.Send(
                new UpdateRestaurantProfileCommand(id, request.Name, request.Description, request.Phone), ct))
                .Match(() => Results.NoContent());

    /// <summary>Les commandes reçues, en attente de décision. Les plus anciennes d'abord.</summary>
    private static async Task<IResult> ListPendingOrdersAsync(
        Guid id, ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.OrderAccept, food, ct)
            ?? (await sender.Send(new ListPendingFoodOrdersQuery(id), ct))
                .Match(items => Results.Ok(items));

    /// <summary>Une commande, quel que soit son état.</summary>
    /// <remarks>
    /// LE `restaurantId` DE L'URL EST CONFRONTÉ À CELUI DE LA COMMANDE dans le
    /// gestionnaire, qui rend « introuvable » dans les deux cas — commande
    /// inexistante ou appartenant à autrui. Distinguer les deux dirait à qui teste
    /// des identifiants lesquels existent.
    /// </remarks>
    private static async Task<IResult> GetOrderAsync(
        Guid restaurantId, Guid foodOrderId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.OrderAccept, food, ct)
            ?? (await sender.Send(new GetFoodOrderQuery(restaurantId, foodOrderId), ct))
                .Match(item => Results.Ok(item));

    private static async Task<IResult> KitchenAsync(
        Guid id, Guid? stationId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.KitchenManage, food, ct)
            ?? (await sender.Send(new GetKitchenBoardQuery(id, stationId), ct))
                .Match(board => Results.Ok(board));

    // ═════════════════════════════════════════════════════════════════════════
    // ACCEPTER ET REFUSER ÉTAIENT LES DEUX SEULES ÉCRITURES PARTENAIRE SANS
    //    CONTRÔLE D'APPARTENANCE.
    //
    // Leurs voisines immédiates — `preparing`, `ready` — passaient bien par
    // `DenyUnlessStaffAsync`. Ces deux-là prenaient le `restaurantId` dans l'URL
    // et se contentaient de lire le porteur du jeton, qu'elles transmettaient au
    // domaine comme ACTEUR. Or `ActorUserId` sert à la traçabilité : il note qui
    // a agi, il n'autorise personne. Le handler ne le compare à rien.
    //
    // Le groupe `/api/food/partner` étant `MapAuthenticatedGroup` — authentifié,
    // aucun rôle — tout compte de la plateforme, un acheteur ou un livreur,
    // pouvait accepter ou refuser la commande de n'importe quel établissement
    // avec deux identifiants. Le refus est le plus coûteux des deux : il annule
    // une commande payée et déclenche le remboursement.
    //
    // LA SIGNATURE DE L'OUBLI : `OrderAccept` et `OrderReject` existent dans
    // `FoodPermission` depuis le §8, avec leurs codes `restaurant.order.*`, et
    // n'étaient réclamées par AUCUNE route. Le modèle de permissions prévoyait
    // exactement ce contrôle ; personne ne l'avait branché. C'est aussi pourquoi
    // le caissier et le cuisinier n'étaient pas distingués ici alors qu'ils le
    // sont partout ailleurs.
    // ═════════════════════════════════════════════════════════════════════════
    private static async Task<IResult> AcceptOrderAsync(
        Guid restaurantId, Guid foodOrderId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.OrderAccept, food, ct) is { } refus)
        {
            return refus;
        }

        // La garde a déjà refusé un jeton sans identifiant exploitable : à ce
        // point, `CurrentUserId` ne peut plus rendre `null`.
        var userId = CurrentUserId(user)!.Value;

        return (await sender.Send(new AcceptFoodOrderCommand(restaurantId, userId, foodOrderId), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> RejectOrderAsync(
        Guid restaurantId, Guid foodOrderId, RejectOrderRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.OrderReject, food, ct) is { } refus)
        {
            return refus;
        }

        var userId = CurrentUserId(user)!.Value;

        if (!Enum.TryParse<FoodRejectionReason>(request.Reason, ignoreCase: true, out var reason))
        {
            return Results.BadRequest(new { error = "food.order.invalid_rejection_reason" });
        }

        return (await sender.Send(new RejectFoodOrderCommand(restaurantId, userId, foodOrderId, reason, request.Comment), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> StartPreparationAsync(
        Guid restaurantId, Guid foodOrderId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.KitchenManage, food, ct)
            ?? (await sender.Send(new StartKitchenTicketCommand(restaurantId, foodOrderId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> MarkReadyAsync(
        Guid restaurantId, Guid foodOrderId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.KitchenManage, food, ct)
            ?? (await sender.Send(new MarkKitchenTicketReadyCommand(restaurantId, foodOrderId), ct))
                .Match(() => Results.NoContent());

    // ──────────────────── Les préalables à la soumission ─────────────────────

    /// <summary>
    /// Remplace la grille de service (§4).
    ///
    /// Sous <c>SettingsManage</c>, comme le minimum de commande et le mode
    /// d'acceptation : les horaires sont un paramètre COMMERCIAL, et
    /// <c>StaffRole</c> l'assume explicitement.
    /// </summary>
    /// <summary>Rattache le logo de l'établissement, ou le retire (les deux nuls).</summary>
    /// <remarks>
    /// `SettingsManage`, PAS `MenuManage` : le logo relève de l'IDENTITÉ de
    /// l'établissement — c'est le mot même de la permission, « identité, horaires ».
    /// Un cuisinier qui gère les plats n'a pas à changer l'enseigne. C'est aussi la
    /// permission qu'exigent `service-hours` et `update`, ses voisines immédiates.
    /// </remarks>
    private static async Task<IResult> SetRestaurantLogoAsync(
        Guid id, RestaurantLogoRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct)
            ?? (await sender.Send(new SetRestaurantLogoCommand(
                id, request.LogoMediaId, request.LogoPublicUrl), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SetServiceHoursAsync(
        Guid id, SetServiceHoursRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct)
            ?? (await sender.Send(new SetServiceHoursCommand(id, request.Hours), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RATTACHE LE DOSSIER VENDEUR QUI ENCAISSERA LES RECETTES.
    ///
    /// CETTE ROUTE EST LA « COUCHE QUI VOIT LES DEUX ».
    ///
    /// <c>Restaurant.AttachPayoutSeller</c> et son gestionnaire le disent tous
    /// deux : Food ne connaît pas Sellers, donc ni l'existence du dossier, ni son
    /// appartenance, ni sa validité ne sont vérifiées en aval. C'est ICI que ces
    /// contrôles doivent vivre — la composition root, seule à référencer les deux
    /// contrats. Sans eux, un restaurateur rattachait le dossier d'un TIERS, dont
    /// il lui suffisait de connaître l'identifiant, et ses recettes partaient sur
    /// le compte Mobile Money de quelqu'un d'autre.
    ///
    /// TROIS CONTRÔLES, ET LE TROISIÈME EN VAUT TROIS
    ///
    ///   1. l'appelant travaille dans CET établissement, avec la permission des
    ///      réglages (<see cref="DenyUnlessStaffAsync"/>) ;
    ///   2. le dossier appartient au PORTEUR DU JETON — pas au propriétaire de
    ///      l'établissement, pas à un identifiant fourni dans le corps ;
    ///   3. le dossier est ACTIF.
    ///
    /// « ACTIF » N'EST PAS UN CONTRÔLE FAIBLE, C'EST LE PLUS FORT DISPONIBLE.
    ///
    /// <c>Seller.Activate()</c> refuse tant que le KYB n'est pas VÉRIFIÉ et tant
    /// qu'AUCUN compte de reversement n'est enregistré. Un vendeur actif a donc
    /// nécessairement les trois. Lire le compte de reversement lui-même serait
    /// impossible d'ici de toute façon : le contrat gRPC ne le transporte pas —
    /// délibérément, c'est un numéro Mobile Money.
    ///
    /// 404 ET NON 403 sur l'appartenance : un 403 confirmerait à qui essaie des
    /// identifiants au hasard que ce dossier vendeur existe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> AttachPayoutSellerAsync(
        Guid id, AttachPayoutSellerRequest request,
        ClaimsPrincipal user, IFoodModuleApi food,
        HBA.Merchants.Contracts.ISellerModuleApi sellers, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct) is { } refus)
        {
            return refus;
        }

        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var dossier = await sellers.GetSellerAsync(request.SellerId, ct);
        if (dossier is null || dossier.UserId != userId)
        {
            return Results.NotFound();
        }

        // Chaîne littérale plutôt que `nameof(SellerStatus.Active)` : ce projet ne
        // référence pas le DOMAINE de Sellers, seulement son contrat, qui porte le
        // statut sous forme de texte (`Status.ToString()`). C'est la même valeur.
        if (!string.Equals(dossier.Status, "Active", StringComparison.Ordinal))
        {
            return Results.Conflict(new
            {
                error = "food.restaurant.payout_seller_not_active",
                message = "Ce dossier vendeur n'est pas actif : faites valider son KYB et "
                    + "enregistrez son compte de reversement avant de le rattacher."
            });
        }

        return (await sender.Send(new AttachRestaurantPayoutSellerCommand(id, request.SellerId), ct))
            .Match(() => Results.NoContent());
    }

    /// <summary>
    /// Rattache le lieu de collecte — l'adresse réelle que le livreur ira trouver.
    /// </summary>
    /// <remarks>
    /// LE LIEU DOIT APPARTENIR AU DOSSIER VENDEUR DE L'ÉTABLISSEMENT.
    ///
    /// Food ne connaît pas Inventory ; <c>AttachRestaurantLocationCommand</c> le
    /// dit et renvoie la vérification à son appelant. Le lien vérifiable est celui
    /// qui existe déjà : <c>FulfillmentLocation.OwnerId</c> désigne un VENDEUR
    /// (« Vendeur si FBS »), et l'établissement en désigne un aussi, par
    /// <c>PayoutSellerId</c>. On exige donc que ce soit le même.
    ///
    /// Conséquence assumée : le dossier de reversement se rattache AVANT le lieu.
    /// L'inverse n'aurait rien à quoi comparer, et le contrôle se réduirait à
    /// « ce lieu existe » — c'est-à-dire à rien, puisque n'importe quel lieu de
    /// n'importe quel vendeur existe.
    /// </remarks>
    private static async Task<IResult> AttachRestaurantLocationAsync(
        Guid id, AttachRestaurantLocationRequest request,
        ClaimsPrincipal user, IFoodModuleApi food,
        HBA.Inventory.Contracts.IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct) is { } refus)
        {
            return refus;
        }

        var restaurant = await food.GetRestaurantAsync(id, ct);
        if (restaurant is null)
        {
            return Results.NotFound();
        }

        if (restaurant.PayoutSellerId is not { } dossier)
        {
            return Results.Conflict(new
            {
                error = "food.restaurant.payout_required",
                message = "Rattachez d'abord le dossier vendeur : c'est lui qui désigne "
                    + "à qui les lieux de collecte peuvent appartenir."
            });
        }

        var lieu = await inventory.GetLocationAsync(request.FulfillmentLocationId, ct);
        if (lieu is null || lieu.OwnerId != dossier)
        {
            // Même règle qu'ailleurs : « introuvable » ne dit pas si le lieu
            // existe chez quelqu'un d'autre.
            return Results.NotFound();
        }

        return (await sender.Send(new AttachRestaurantLocationCommand(id, request.FulfillmentLocationId), ct))
            .Match(() => Results.NoContent());
    }

    // ───────────────────────────── Cycle de vie ──────────────────────────────

    /// <summary>Le restaurateur soumet son dossier à validation.</summary>
    private static async Task<IResult> SubmitRestaurantAsync(
        Guid id, ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct)
            ?? (await sender.Send(new SubmitRestaurantCommand(id), ct)).Match(() => Results.NoContent());

    // ───────────────────────────── Modération ────────────────────────────────

    /// <remarks>
    /// L'approbation déclenche `RestaurantApprovedIntegrationEvent`, que
    /// identity-service consomme pour attribuer le rôle `FoodPartner`. Sans cette
    /// route, le rôle n'était jamais attribué — et l'application restaurateur
    /// restait fermée à son propre fondateur.
    /// </remarks>
    private static async Task<IResult> ApproveRestaurantAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveRestaurantCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RejectRestaurantAsync(
        Guid id, ModerationReasonRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectRestaurantCommand(id, request?.Reason), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> SuspendRestaurantAsync(
        Guid id, ModerationReasonRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendRestaurantCommand(id, request?.Reason), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> LiftRestaurantSuspensionAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new LiftRestaurantSuspensionCommand(id), ct)).Match(() => Results.NoContent());

    // ─────────────────────────────── La carte ────────────────────────────────
    //
    // Les six routes passent par la même garde : appartenance au personnel de CET
    // établissement, plus la permission `restaurant.menu.manage`. Un cuisinier
    // prépare la carte, il ne la compose pas.

    private static async Task<IResult> CreateMenuAsync(
        Guid restaurantId, CreateMenuRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new CreateMenuCommand(restaurantId, request.Name, request.DisplayOrder), ct))
                .Match(id => Results.Created(
                    $"/api/food/partner/restaurants/{restaurantId}/menus/{id}", new { id }));

    private static async Task<IResult> CreateCategoryAsync(
        Guid restaurantId, Guid menuId, CreateCategoryRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new CreateCategoryCommand(restaurantId, menuId, request.Name, request.DisplayOrder), ct))
                .Match(id => Results.Created(
                    $"/api/food/partner/restaurants/{restaurantId}/categories/{id}", new { id }));

    private static async Task<IResult> CreateMenuItemAsync(
        Guid restaurantId, Guid categoryId, CreateMenuItemRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new CreateMenuItemCommand(restaurantId, categoryId, request.Name, request.BasePrice), ct))
                .Match(id => Results.Created(
                    $"/api/food/partner/restaurants/{restaurantId}/items/{id}", new { id }));

    /// <summary>Rattache une photo à un article, ou la retire.</summary>
    /// <remarks>
    /// `null` RETIRE LA PHOTO, et c'est volontaire plutôt qu'un DELETE séparé :
    /// `SetImage` traite déjà les deux cas, et une seconde route dupliquerait la
    /// garde d'appartenance pour un geste que le domaine considère comme le même.
    /// </remarks>
    private static async Task<IResult> SetItemImageAsync(
        Guid restaurantId, Guid itemId, ItemImageRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
        ?? (await sender.Send(new SetMenuItemImageCommand(
                restaurantId, itemId, request.ImageMediaId, request.ImagePublicUrl), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ChangeItemPriceAsync(
        Guid restaurantId, Guid itemId, ChangePriceRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new ChangeMenuItemPriceCommand(restaurantId, itemId, request.BasePrice), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> AddOptionGroupAsync(
        Guid restaurantId, Guid itemId, AddOptionGroupRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new AddOptionGroupCommand(
                restaurantId, itemId, request.Name,
                request.MinSelections, request.MaxSelections, request.DisplayOrder), ct))
                .Match(id => Results.Created(
                    $"/api/food/partner/restaurants/{restaurantId}/items/{itemId}/option-groups/{id}",
                    new { id }));

    private static async Task<IResult> AddOptionAsync(
        Guid restaurantId, Guid itemId, Guid groupId, AddOptionRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new AddOptionCommand(
                restaurantId, itemId, groupId, request.Name, request.PriceDelta), ct))
                .Match(id => Results.Created(
                    $"/api/food/partner/restaurants/{restaurantId}/items/{itemId}"
                    + $"/option-groups/{groupId}/options/{id}", new { id }));

    // ──────────────────────── La carte : édition ─────────────────────────────
    //
    // Même garde que la création : appartenance au personnel de CET
    // établissement, plus `restaurant.menu.manage`. Toutes rendent 204 — le
    // client relit la carte, qui est la seule projection qui fasse foi.

    private static async Task<IResult> RenameMenuAsync(
        Guid restaurantId, Guid menuId, RenameRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new RenameMenuCommand(restaurantId, menuId, request.Name, request.Description), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SetMenuVisibilityAsync(
        Guid restaurantId, Guid menuId, VisibilityRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new SetMenuVisibilityCommand(restaurantId, menuId, request.Active), ct))
                .Match(() => Results.NoContent());

    /// <remarks>
    /// REFUSÉE TANT QUE LA CARTE PORTE DES SECTIONS — c'est
    /// <c>DeleteMenuCommand</c> qui le décide, et il rend un <c>Conflict</c>
    /// explicite. Les sections référencent la carte sans lui appartenir :
    /// supprimer la carte ne les supprimerait pas, elle les ORPHELINERAIT, et la
    /// projection les ferait disparaître des deux vues avec tous leurs articles.
    /// L'application doit donc présenter ce 409 comme une instruction — « videz
    /// d'abord la carte » — et non comme une panne.
    /// </remarks>
    private static async Task<IResult> DeleteMenuAsync(
        Guid restaurantId, Guid menuId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new DeleteMenuCommand(restaurantId, menuId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> RenameCategoryAsync(
        Guid restaurantId, Guid categoryId, RenameRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new RenameCategoryCommand(restaurantId, categoryId, request.Name, request.Description), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SetCategoryVisibilityAsync(
        Guid restaurantId, Guid categoryId, VisibilityRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new SetCategoryVisibilityCommand(restaurantId, categoryId, request.Active), ct))
                .Match(() => Results.NoContent());

    /// <remarks>
    /// `PUT` SUR UNE POSITION, ET NON UNE LISTE RÉORDONNÉE ENTIÈRE.
    ///
    /// Le domaine expose <c>ReorderCategoryCommand</c>, qui déplace UNE section.
    /// Faire porter au client l'ordre complet obligerait à résoudre un conflit
    /// dès que deux membres du personnel réordonnent la carte en même temps —
    /// le dernier écrasant le travail de l'autre en silence.
    /// </remarks>
    private static async Task<IResult> ReorderCategoryAsync(
        Guid restaurantId, Guid categoryId, PositionRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(
                new ReorderCategoryCommand(restaurantId, categoryId, request.DisplayOrder), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteCategoryAsync(
        Guid restaurantId, Guid categoryId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new DeleteCategoryCommand(restaurantId, categoryId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> UpdateMenuItemAsync(
        Guid restaurantId, Guid itemId, UpdateMenuItemRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new UpdateMenuItemCommand(
                restaurantId, itemId, request.Name, request.Description, request.DisplayOrder), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// Les trois positions de l'interrupteur de disponibilité d'un plat.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// « ÉPUISÉ AUJOURD'HUI » ET « INDISPONIBLE » NE SONT PAS SYNONYMES, ET
    ///    LES CONFONDRE COÛTE CHER DANS LES DEUX SENS.
    ///
    ///   • <c>sold_out_today</c> — le poisson est parti à 13 h. L'échéance est
    ///     calculée par le domaine à partir des horaires de l'établissement, et
    ///     le plat REVIENT SEUL au service suivant. Demander la date à un
    ///     cuisinier en plein service, sur un téléphone, serait la garantie
    ///     qu'il ne le fasse pas.
    ///   • <c>unavailable</c> — le plat sort de la carte jusqu'à nouvel ordre.
    ///     Il NE revient PAS seul.
    ///
    /// Marquer « indisponible » ce qui n'était qu'épuisé du jour fait disparaître
    /// un plat de la vitrine jusqu'à ce que quelqu'un s'en aperçoive. L'inverse
    /// remet en vente, le lendemain matin, un plat qu'on avait retiré.
    ///
    /// 400 SUR UN ÉTAT INCONNU, JAMAIS UN REPLI SILENCIEUX. Un défaut à
    /// « disponible » sur une faute de frappe remettrait en vente un plat qu'on
    /// venait de retirer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult> SetItemAvailabilityAsync(
        Guid restaurantId, Guid itemId, ItemAvailabilityRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
    {
        if (await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct) is { } refus)
        {
            return refus;
        }

        var resultat = request.State?.Trim().ToLowerInvariant() switch
        {
            "available" => await sender.Send(new MarkItemAvailableCommand(restaurantId, itemId), ct),
            "sold_out_today" => await sender.Send(new MarkItemSoldOutTodayCommand(restaurantId, itemId), ct),
            "unavailable" => await sender.Send(new MarkItemUnavailableCommand(restaurantId, itemId), ct),
            _ => null
        };

        return resultat is null
            ? Results.BadRequest(new
            {
                error = "food.menu.invalid_availability_state",
                message = "États acceptés : available, sold_out_today, unavailable."
            })
            : resultat.Match(() => Results.NoContent());
    }

    /// <remarks>
    /// CE N'EST PAS « ARRÊTER DE VENDRE CE PLAT » — pour cela, l'état
    /// <c>unavailable</c> ci-dessus, qui le garde en base prêt à revenir. La
    /// suppression sert aux ERREURS DE SAISIE : le doublon, la faute de frappe,
    /// le plat créé dans la mauvaise section.
    /// </remarks>
    private static async Task<IResult> DeleteMenuItemAsync(
        Guid restaurantId, Guid itemId,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, restaurantId, FoodPermission.MenuManage, food, ct)
            ?? (await sender.Send(new DeleteMenuItemCommand(restaurantId, itemId), ct))
                .Match(() => Results.NoContent());

    // ─────────────────────── Interruption du service ─────────────────────────

    private static async Task<IResult> PauseRestaurantAsync(
        Guid id, PauseRestaurantRequest request,
        ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct)
            ?? (await sender.Send(new PauseRestaurantCommand(id, request.Minutes), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> ResumeRestaurantAsync(
        Guid id, ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.SettingsManage, food, ct)
            ?? (await sender.Send(new ResumeRestaurantCommand(id), ct))
                .Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record RegisterRestaurantRequest(string Name, string? Description, string Phone);

    public sealed record UpdateRestaurantRequest(string Name, string? Description, string Phone);

    /// <param name="Hours">
    /// La grille ENTIÈRE : elle REMPLACE la précédente (voir
    /// <c>Restaurant.SetServiceHours</c>). Jour en anglais invariant
    /// (« Monday »…), heures au format « HH:mm », en heure locale du Bénin.
    /// </param>
    public sealed record SetServiceHoursRequest(IReadOnlyList<ServiceHoursInput> Hours);

    /// <param name="SellerId">Le dossier vendeur — celui du PORTEUR DU JETON, et vérifié comme tel.</param>
    /// <param name="LogoPublicUrl">
    /// L'adresse rendue par media-service au dépôt. Voir `ItemImageRequest` pour
    /// pourquoi elle vient du client et pourquoi ce n'est pas une faille.
    /// </param>
    public sealed record RestaurantLogoRequest(Guid? LogoMediaId, string? LogoPublicUrl);

    public sealed record AttachPayoutSellerRequest(Guid SellerId);

    /// <param name="FulfillmentLocationId">Un lieu d'Inventory appartenant au dossier de reversement.</param>
    public sealed record AttachRestaurantLocationRequest(Guid FulfillmentLocationId);

    public sealed record RejectOrderRequest(string Reason, string? Comment);

    /// <summary>Corps facultatif : un refus sans motif reste un refus.</summary>
    public sealed record ModerationReasonRequest(string? Reason);

    public sealed record CreateMenuRequest(string Name, int DisplayOrder);

    public sealed record CreateCategoryRequest(string Name, int DisplayOrder);

    /// <param name="BasePrice">En XOF, hors options.</param>
    public sealed record CreateMenuItemRequest(string Name, decimal BasePrice);

    public sealed record ChangePriceRequest(decimal BasePrice);

    /// <param name="MinSelections">0 = groupe facultatif.</param>
    public sealed record AddOptionGroupRequest(
        string Name, int MinSelections, int MaxSelections, int DisplayOrder);

    /// <param name="PriceDelta">Écart au prix de base, positif ou négatif.</param>
    public sealed record AddOptionRequest(string Name, decimal PriceDelta);

    /// <summary>
    /// Renommer une carte ou une section.
    /// </summary>
    /// <remarks>
    /// UN SEUL CONTRAT POUR LES DEUX, PARCE QUE LES DEUX COMMANDES PRENNENT
    /// EXACTEMENT LES MÊMES CHAMPS (<c>RenameMenuCommand</c>,
    /// <c>RenameCategoryCommand</c>). En déclarer deux identiques garantirait
    /// qu'ils divergent le jour où l'un des deux évoluera, sans que rien ne le
    /// signale.
    ///
    /// <c>Description</c> est nullable et REMPLACE : envoyer <c>null</c> efface.
    /// </remarks>
    public sealed record RenameRequest(string Name, string? Description);

    /// <param name="Active">Faux masque de la vitrine SANS rien supprimer.</param>
    public sealed record VisibilityRequest(bool Active);

    /// <param name="DisplayOrder">Rang d'affichage. Les rangs égaux sont départagés par le nom.</param>
    public sealed record PositionRequest(int DisplayOrder);

    /// <param name="DisplayOrder">
    /// ABSENT DU CORPS ⇒ RANG INCHANGÉ, et il l'était toujours.
    ///
    /// Le champ était `int` : un corps JSON sans `displayOrder` se liait à 0, et
    /// renommer un plat le remontait en tête de section. Aucune application ne
    /// pouvait faire mieux — `MenuItemView` n'expose pas le rang, donc rien à
    /// relire pour le renvoyer.
    /// </param>
    public sealed record UpdateMenuItemRequest(
        string Name, string? Description, int? DisplayOrder = null);

    /// <param name="State">
    /// <c>available</c>, <c>sold_out_today</c> ou <c>unavailable</c>. Voir
    /// <c>SetItemAvailabilityAsync</c> : les deux derniers ne sont PAS synonymes.
    /// </param>
    public sealed record ItemAvailabilityRequest(string? State);

    /// <param name="ImageMediaId">
    /// L'identifiant rendu par media-service après dépôt. `null` retire la photo.
    ///
    /// PAS UNE URL. Une URL laisserait pointer vers un domaine tiers, et la
    /// photo disparaîtrait le jour où celui-ci ferme — sans que personne ne puisse
    /// la retrouver, ni même savoir qu'elle a existé.
    /// </param>
    /// <summary>
    /// La photo d'un article : son identifiant de média ET son adresse publique.
    /// </summary>
    /// <remarks>
    /// L'URL VIENT DU CLIENT, ET CE N'EST PAS UNE FAILLE — C'EST LE PATRON DE
    /// CATALOG (`POST /products/{id}/media` prend `mediaId` et `url` de la même façon).
    ///
    /// L'application dépose le fichier sur media-service, qui lui rend l'adresse ;
    /// elle la retransmet. Falsifier ce champ ne donne accès à rien : l'URL n'autorise
    /// aucune lecture, elle en désigne une, et le seul dommage possible est d'afficher
    /// une image tierce sur SA PROPRE carte — que le restaurateur voit immédiatement.
    ///
    /// L'alternative — que food-service résolve l'adresse lui-même — demande un client
    /// gRPC vers media-service sur la lecture la plus chaude de l'application. Voir
    /// `MenuItem.ImagePublicUrl`.
    /// </remarks>
    public sealed record ItemImageRequest(Guid? ImageMediaId, string? ImagePublicUrl);

    /// <param name="Minutes">
    /// Durée de la pause. OBLIGATOIRE : une interruption sans échéance qu'on
    /// oublie de lever retire l'établissement de la vitrine pour la soirée sans
    /// que personne le remarque. Les bornes sont celles du domaine.
    /// </param>
    public sealed record PauseRestaurantRequest(int Minutes);
}
