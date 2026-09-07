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
        publicFood.MapGet("/restaurants", ListStorefrontAsync).AllowAnonymous();
        publicFood.MapGet("/restaurants/{id:guid}", GetPublicRestaurantAsync).AllowAnonymous();
        publicFood.MapGet("/restaurants/{id:guid}/menu", GetPublicMenuAsync).AllowAnonymous();

        var partner = app.MapAuthenticatedGroup("/api/food/partner").WithTags("Food · Partner");
        // PRÉALABLE AU SÉLECTEUR D'ACTIVITÉ DE HBA PARTNER.
        partner.MapGet("/me", MyRestaurantAsync).WithName("GetMyRestaurant");
        partner.MapPost("/restaurants", RegisterRestaurantAsync);
        partner.MapPut("/restaurants/{id:guid}", UpdateRestaurantAsync);

        // LES TROIS ROUTES QUI MANQUAIENT ENTRE « CRÉER » ET « SOUMETTRE ».
        partner.MapPut("/restaurants/{id:guid}/service-hours", SetServiceHoursAsync);
        partner.MapPut("/restaurants/{id:guid}/logo", SetRestaurantLogoAsync);
        partner.MapPut("/restaurants/{id:guid}/payout-seller", AttachPayoutSellerAsync);
        partner.MapPut("/restaurants/{id:guid}/location", AttachRestaurantLocationAsync);

        partner.MapPost("/restaurants/{id:guid}/submit", SubmitRestaurantAsync);
        partner.MapGet("/restaurants/{id:guid}/menu", GetOwnerMenuAsync);
        // LA FILE D'ACCEPTATION — LES DEUX ROUTES QUI MANQUAIENT (tâche #227).
        partner.MapGet("/restaurants/{id:guid}/orders", ListPendingOrdersAsync);
        partner.MapGet("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}", GetOrderAsync);

        partner.MapGet("/restaurants/{id:guid}/kitchen", KitchenAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/accept", AcceptOrderAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/reject", RejectOrderAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/preparing", StartPreparationAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/orders/{foodOrderId:guid}/ready", MarkReadyAsync);

        // LA CARTE — ÉCRITE DEPUIS DES MOIS, JAMAIS EXPOSÉE.
        partner.MapPost("/restaurants/{restaurantId:guid}/menus", CreateMenuAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/menus/{menuId:guid}/categories", CreateCategoryAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/categories/{categoryId:guid}/items", CreateMenuItemAsync);
        partner.MapPut("/restaurants/{restaurantId:guid}/items/{itemId:guid}/price", ChangeItemPriceAsync);

        // LA PHOTO D'UN ARTICLE N'AVAIT AUCUNE ROUTE, alors que tout le reste
        // existait : `MenuItem.ImageMediaId`, `MenuItem.SetImage`,
        // `SetMenuItemImageCommand` et son gestionnaire.
        partner.MapPut("/restaurants/{restaurantId:guid}/items/{itemId:guid}/image", SetItemImageAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/items/{itemId:guid}/option-groups", AddOptionGroupAsync);
        partner.MapPost("/restaurants/{restaurantId:guid}/items/{itemId:guid}/option-groups/{groupId:guid}/options", AddOptionAsync);

        // LA CARTE ÉTAIT EN CRÉATION SEULE.
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

        // FERMER LA CUISINE UN MOMENT — SANS FERMER BOUTIQUE.
        partner.MapPost("/restaurants/{id:guid}/pause", PauseRestaurantAsync);
        partner.MapPost("/restaurants/{id:guid}/resume", ResumeRestaurantAsync);

        // MODÉRATION — GROUPE ADMIN, ET C'EST UNE CORRECTION.
        var moderation = app.MapAdminGroup("/api/food/admin").WithTags("Food · Modération");

        moderation.MapGet("/restaurants/pending", ListPendingRestaurantsAsync);
        moderation.MapPost("/restaurants/{id:guid}/approve", ApproveRestaurantAsync);
        moderation.MapPost("/restaurants/{id:guid}/reject", RejectRestaurantAsync);
        moderation.MapPost("/restaurants/{id:guid}/suspend", SuspendRestaurantAsync);
        moderation.MapPost("/restaurants/{id:guid}/lift-suspension", LiftRestaurantSuspensionAsync);

        return app;
    }

    // LE CONTRÔLE D'APPARTENANCE QUI MANQUAIT.
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

    /// <summary>La vitrine, paginée.</summary>
    private static async Task<IResult> ListStorefrontAsync(
        int? page, int? pageSize, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListStorefrontQuery(page ?? 1, pageSize ?? 20), ct))
            .Match(cartes => Results.Ok(cartes));

    /// <summary>La fiche publique d'un établissement.</summary>
    private static async Task<IResult> GetPublicRestaurantAsync(
        Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPublicRestaurantQuery(id), ct))
            .Match(restaurant => Results.Ok(restaurant));

    /// <summary>L'établissement du compte connecté, et son rôle dedans.</summary>
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

    /// <summary>Les commandes reçues, en attente de décision.</summary>
    private static async Task<IResult> ListPendingOrdersAsync(
        Guid id, ClaimsPrincipal user, IFoodModuleApi food, ISender sender, CancellationToken ct)
        => await DenyUnlessStaffAsync(user, id, FoodPermission.OrderAccept, food, ct)
            ?? (await sender.Send(new ListPendingFoodOrdersQuery(id), ct))
                .Match(items => Results.Ok(items));

    /// <summary>Une commande, quel que soit son état.</summary>
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

    /// <summary>Remplace la grille de service (§4).</summary>
    /// <summary>Rattache le logo de l'établissement, ou le retire (les deux nuls).</summary>
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
        // statut sous forme de texte (`Status.ToString()`).
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
            // Même règle qu'ailleurs : « introuvable » ne dit pas si le lieu existe
            // chez quelqu'un d'autre.
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

    /// <summary>Les trois positions de l'interrupteur de disponibilité d'un plat.</summary>
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
    /// La grille ENTIÈRE : elle REMPLACE la précédente (voir <c>
    /// Restaurant.SetServiceHours</c>).
    /// </param>
    public sealed record SetServiceHoursRequest(IReadOnlyList<ServiceHoursInput> Hours);

    /// <param name="SellerId">
    /// Le dossier vendeur — celui du PORTEUR DU JETON, et vérifié comme tel.
    /// </param>
    /// <param name="LogoPublicUrl">L'adresse rendue par media-service au dépôt.</param>
    public sealed record RestaurantLogoRequest(Guid? LogoMediaId, string? LogoPublicUrl);

    public sealed record AttachPayoutSellerRequest(Guid SellerId);

    /// <param name="FulfillmentLocationId">
    /// Un lieu d'Inventory appartenant au dossier de reversement.
    /// </param>
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

    /// <summary>Renommer une carte ou une section.</summary>
    public sealed record RenameRequest(string Name, string? Description);

    /// <param name="Active">Faux masque de la vitrine SANS rien supprimer.</param>
    public sealed record VisibilityRequest(bool Active);

    /// <param name="DisplayOrder">
    /// Rang d'affichage. Les rangs égaux sont départagés par le nom.
    /// </param>
    public sealed record PositionRequest(int DisplayOrder);

    /// <param name="DisplayOrder">ABSENT DU CORPS ⇒ RANG INCHANGÉ, et il l'était toujours.</param>
    public sealed record UpdateMenuItemRequest(
        string Name, string? Description, int? DisplayOrder = null);

    /// <param name="State"><c>available</c>, <c>sold_out_today</c> ou <c>unavailable</c>.</param>
    public sealed record ItemAvailabilityRequest(string? State);

    /// <param name="ImageMediaId">L'identifiant rendu par media-service après dépôt.</param>
    /// <summary>La photo d'un article : son identifiant de média ET son adresse publique.</summary>
    public sealed record ItemImageRequest(Guid? ImageMediaId, string? ImagePublicUrl);

    /// <param name="Minutes">
    /// Durée de la pause. OBLIGATOIRE : une interruption sans échéance qu'on oublie
    /// de lever retire l'établissement de la vitrine pour la soirée sans que
    /// personne le remarque.
    /// </param>
    public sealed record PauseRestaurantRequest(int Minutes);
}
