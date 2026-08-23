using System.Security.Claims;
using HBA.Inventory.Application.Locations.Commands;
using HBA.Inventory.Application.Locations.Queries;
using HBA.Inventory.Application.Stock.Commands;
using HBA.Inventory.Application.Stock.Queries;
using HBA.Inventory.Contracts;
using HBA.Merchants.Contracts;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Inventory.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Inventory.</summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════════════════════════════════════════════
        // TOUT LE STOCK DE LA PLACE DE MARCHÉ SE PILOTAIT AVEC UN COMPTE
        // ACHETEUR.
        //
        // Les dix-sept routes tenaient dans un seul groupe « authentifié ». Ce
        // que cela donnait, en clair :
        //
        //   POST /items/{id}/adjust     → un delta négatif met le stock d'un
        //                                 concurrent à zéro. Ses offres
        //                                 disparaissent de la vitrine.
        //   POST /reservations/release  → la réservation d'une commande PAYÉE
        //                                 est levée : la quantité repart à la
        //                                 vente et la commande est expédiée sur
        //                                 un stock qui n'existe plus.
        //   POST /reservations/confirm  → consomme le stock d'une commande qui
        //                                 n'a jamais été réglée.
        //   DELETE /locations/{id}      → supprime le lieu d'enlèvement d'un
        //                                 vendeur. Plus aucune course ne peut
        //                                 être créée pour ses colis.
        //   GET /low-stock, /locations  → la liste complète des entrepôts et des
        //                                 ruptures de TOUS les vendeurs.
        //
        // FERMER CES ROUTES NE CASSE AUCUNE SAGA — VÉRIFIÉ AVANT, PAS APRÈS.
        //
        // Réserver, libérer et confirmer entre services passent par
        // `InventoryGrpcService` (ReserveStock / ReleaseReservation /
        // ConfirmReservation), sur le port interne, derrière
        // `InternalCallServerInterceptor`. Aucun service n'appelle ces chemins
        // HTTP : les jumeaux HTTP sont une trappe d'exploitation, pas le chemin
        // nominal. Le seul appelant HTTP connu de tout le fichier est la
        // passerelle, sur `/availability/{sku}` (voir `InventoryClient`), qui
        // reste dans le groupe authentifié.
        // ═════════════════════════════════════════════════════════════════════
        var inventory = app.MapAuthenticatedGroup("/api/inventory").WithTags("Inventory");
        inventory.MapGet("/owners/{ownerId:guid}/locations", ListOwnerLocationsAsync);
        inventory.MapGet("/items/{id:guid}", GetItemAsync);
        inventory.MapGet("/items/sku/{sku}", ListBySkuAsync);
        inventory.MapPost("/items/by-locations", ListByLocationsAsync);
        inventory.MapGet("/availability/{sku}", AvailabilityAsync);

        // CES LECTURES RESTENT TRANSVERSES, ET C'EST UN RESTE ASSUMÉ.
        //
        // Hors `/availability/{sku}`, qui sert la fiche produit, un inscrit qui
        // connaît un `ownerId`, un SKU ou un identifiant de lieu lit encore les
        // quantités d'un vendeur qui n'est pas lui.
        //
        // L'EXCUSE A DISPARU AVEC VEN11, PAS LA FUITE.
        //
        // Ce commentaire disait que le contrôle « exigerait la correspondance
        // compte → vendeur, qu'inventory-service ne référence pas ». Il la
        // référence désormais : `DenyUnlessOwnerAsync` existe et garde les sept
        // écritures. Rien n'empêche plus techniquement de scoper ces quatre
        // lectures — c'est devenu un travail à faire, et non une impossibilité.
        //
        // DEUX D'ENTRE ELLES EXIGENT DÉSORMAIS `INVENTORY_VIEW`.
        //
        // `/items/sku/{sku}` et `/items/by-locations` filtraient sur les lieux du
        // vendeur sans exiger de capacité : un chargé de clientèle refusé sur
        // `GET /items/{id}` obtenait les mêmes quantités par l'une des deux. Le
        // contrôle vit dans `MesLieuxAsync`, qui rend un ensemble VIDE plutôt qu'un
        // refus — ces routes servent aussi la fiche produit et le panier client, et
        // un 403 y casserait la vitrine.
        //
        // Restent transverses : `/availability/{sku}`, qui ne rend qu'un total sans
        // lieu ni propriétaire, et `/owners/{ownerId}/locations`, désormais comparée
        // au vendeur de l'appelant.

        // ═════════════════════════════════════════════════════════════════════
        // GOUVERNANCE DU STOCK — MÊME PRÉFIXE, AUTRE POLITIQUE.
        //
        // Deux groupes sur `/api/inventory` : les chemins publics ne changent
        // pas, seule l'exigence de rôle change route par route. Aucun gabarit
        // n'est en doublon entre les deux groupes.
        // ═════════════════════════════════════════════════════════════════════
        var admin = app.MapAdminGroup("/api/inventory").WithTags("Inventory · Admin");
        admin.MapGet("/locations", ListLocationsAsync);
        admin.MapGet("/low-stock", LowStockAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LES RÉSERVATIONS RESTENT À L'ADMINISTRATION, ET DÉFINITIVEMENT.
        //
        // Ce ne sont pas des gestes de vendeur : réserver, libérer et confirmer
        // appartiennent à la saga de commande, et le chemin nominal est
        // `InventoryGrpcService` sur le port interne. Ces trois routes HTTP sont
        // une trappe d'exploitation. Les ouvrir au vendeur lui donnerait prise
        // sur le stock engagé par les commandes d'autrui — et sur les siennes,
        // ce qui est pire : libérer la réservation d'une commande payée fait
        // repartir la quantité à la vente.
        // ═════════════════════════════════════════════════════════════════════
        admin.MapPost("/reservations", ReserveAsync);
        admin.MapPost("/reservations/release", ReleaseAsync);
        admin.MapPost("/reservations/confirm", ConfirmAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LE STOCK REVIENT À CELUI QUI LE DÉTIENT (VEN11, phase 2).
        //
        // Ces sept routes vivaient sous `MapAdminGroup`. Un vendeur recevait 403
        // sur son PROPRE stock : il ne pouvait ni recevoir une livraison, ni
        // corriger un inventaire, ni déclarer un lieu d'expédition. L'écran de
        // stock de l'application existait et n'a jamais rien pu écrire.
        //
        // CE N'ÉTAIT PAS UN MAUVAIS RÉGLAGE, C'ÉTAIT UNE IMPOSSIBILITÉ.
        //
        // Le commentaire du groupe de lecture, plus haut, l'énonçait déjà : le
        // contrôle de propriété « exigerait la correspondance compte → vendeur,
        // détenue par merchant-service, qu'inventory-service ne référence pas ».
        // Ranger les écritures à l'administration était le seul choix sûr TANT
        // QUE cette dépendance n'existait pas. Elle existe désormais
        // (`AddMerchantsGrpcClient` dans `Program.cs`), et c'est elle — pas le
        // déplacement de ces lignes — qui constitue le travail.
        //
        // L'ORDRE COMPTE : la dépendance D'ABORD, la garde ENSUITE, le
        // déplacement EN DERNIER. Déplacer d'abord aurait ouvert, le temps d'un
        // commit, une écriture libre sur le stock de n'importe qui.
        //
        // LA PROPRIÉTÉ D'UN ARTICLE PASSE PAR SON LIEU.
        //
        // `InventoryItem` ne porte AUCUN propriétaire — seulement un `Sku` et un
        // `LocationId`. C'est `FulfillmentLocation.OwnerId` qui désigne un
        // VENDEUR. Toute la garde repose donc sur cette chaîne :
        //
        //     jeton → userId → sellerId → location.OwnerId → items du lieu
        //
        // Et `OwnerId` est NULLABLE : un lieu sans propriétaire est un entrepôt
        // de plateforme. Aucun vendeur ne doit pouvoir y écrire — d'où le refus
        // explicite du cas `null` dans `DenyUnlessOwnerAsync`.
        // ═════════════════════════════════════════════════════════════════════
        // `MapSellerGroup` ET NON `MapAuthenticatedGroup` — ALIGNEMENT DE L'AUDIT.
        //
        // Ces sept écritures portent chacune leur garde, donc rien n'était ouvert.
        // Mais c'est précisément l'état que `MapSellerGroup` existe pour remplacer :
        // « la protection était une discipline, pas une barrière ». La huitième
        // route ajoutée ici sans garde aurait été atteignable par tout acheteur, et
        // c'est le mode de défaillance qui s'est déjà produit cinq fois dans ce
        // dépôt. Catalog, merchants et review sont sous `MapSellerGroup` ; ce groupe
        // et celui d'order étaient les deux derniers à ne pas l'être.
        var seller = app.MapSellerGroup("/api/inventory").WithTags("Inventory · Vendeur");
        seller.MapPost("/locations", CreateLocationAsync);
        seller.MapPut("/locations/{id:guid}/address", UpdateLocationAsync);
        seller.MapDelete("/locations/{id:guid}", DeleteLocationAsync);
        seller.MapPost("/items", CreateItemAsync);
        seller.MapPost("/items/{id:guid}/receive", ReceiveStockAsync);
        seller.MapPost("/items/{id:guid}/adjust", AdjustStockAsync);
        seller.MapPut("/items/{id:guid}/reorder-threshold", SetThresholdAsync);

        // ═════════════════════════════════════════════════════════════════════
        // DEUX PERMISSIONS QUI NE GARDAIENT RIEN DEPUIS LE PREMIER JOUR
        //    (ISSUE-044).
        //
        // `STOCK_MOVEMENT_VIEW` et `INVENTORY_TRANSFER` sont déclarées, attribuées
        // à `STORE_ADMIN` et `INVENTORY_MANAGER` — dont la description dit
        // « Stocks, ajustements, transferts » — et aucune route ne les exigeait.
        // Le mot « transfert » n'apparaissait nulle part dans ce service, et rien
        // ne gardait trace d'un ajustement : `AdjustOnHand(int delta)` ne prenait
        // ni acteur ni motif.
        //
        // LE TRANSFERT EST GARDÉ DEUX FOIS, SOURCE ET DESTINATION.
        //
        // Une seule garde suffirait à protéger le stock qu'on retire ; elle ne
        // dirait rien de l'entrepôt où il arrive. Un vendeur transférerait alors sa
        // marchandise chez un tiers — ou, plus vraisemblablement, y enverrait par
        // erreur un stock qu'il ne récupérerait jamais.
        // ═════════════════════════════════════════════════════════════════════
        seller.MapGet("/items/{id:guid}/movements", ListMovementsAsync);
        seller.MapPost("/transfers", TransferStockAsync);

        return app;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LA GARDE DE PROPRIÉTÉ — UNE SEULE, POUR LES SEPT ROUTES.
    //
    // 404 ET NON 403, comme partout ailleurs dans cette plateforme : un 403
    // confirmerait à qui tâtonne que le lieu — ou l'article — existe.
    //
    // L'ADMINISTRATION PASSE OUTRE, ET C'EST NÉCESSAIRE. Un modérateur doit
    // pouvoir corriger le stock d'un vendeur injoignable. Le contrôle ne
    // s'applique donc qu'à défaut du rôle.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Le contexte d'accès du porteur du jeton — vendeur, capacités, boutiques —
    /// ou <c>null</c> s'il n'a aucun rattachement commerçant.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE N'EST PLUS `GetSellerByUserIdAsync`, ET LA DIFFÉRENCE EST TOUT LE LOT.
    ///
    /// `GetSellerByUserIdAsync` ne résout QUE les propriétaires : elle lit la
    /// colonne `UserId` du dossier vendeur. Un membre d'équipe — un gestionnaire
    /// de stock recruté par le vendeur — n'a pas de dossier à son nom, donc cette
    /// méthode rendait `null` et la garde répondait 404. L'écran de stock
    /// fonctionnait pour le patron et pour personne d'autre.
    ///
    /// `GetAccessAsync` rend le même `SellerId` pour le propriétaire, ET le
    /// rattachement du membre — accompagné de ses permissions. La propriété seule
    /// ne suffit donc plus à autoriser : elle dit QUEL vendeur, la capacité dit
    /// SI l'on peut. Les deux contrôles sont distincts et tous deux obligatoires.
    ///
    /// LA RÉPONSE EST MISE EN CACHE CÔTÉ seller-service, PAS ICI.
    ///
    /// Chaque garde appelle cette méthode, parfois deux fois sur une même requête
    /// (`DenyUnlessOwnerOfItemAsync` remonte à `DenyUnlessOwnerAsync`). Le cache
    /// vit dans `MerchantAccessApi`, purgé dans le même `SaveChangesAsync` que la
    /// mutation qui l'invalide : un cache local ici serait une seconde copie que
    /// rien ne purgerait, et une révocation mettrait deux minutes à mordre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<MerchantAccess?> AccesVendeurAsync(
        ClaimsPrincipal user, IMerchantAccessApi access, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? null
            : await access.GetAccessAsync(userId, ct);

    private static bool IsAdmin(ClaimsPrincipal user)
        => user.IsInRole("Admin") || user.IsInRole("Moderator");

    /// <summary>
    /// Refuse si <paramref name="locationId"/> n'appartient pas à l'appelant, ou si
    /// celui-ci ne porte pas <paramref name="capacite"/>.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX QUESTIONS, DEUX RÉPONSES HTTP DIFFÉRENTES — ET C'EST VOULU.
    ///
    ///   « ce lieu n'est pas à vous »      → 404, comme avant. Un 403 confirmerait
    ///                                       l'existence du lieu à qui tâtonne des
    ///                                       identifiants.
    ///   « il est à vous, mais pas vous »  → 403 enveloppé, avec la capacité
    ///                                       manquante dans `error.details`.
    ///
    /// Rendre 404 dans le second cas serait cruel et faux : le membre VOIT ce lieu
    /// dans son application, il vient d'y cliquer. Lui répondre « introuvable »
    /// enverrait le vendeur chercher un bug dans les données là où il n'y a qu'un
    /// rôle à élargir — et le support n'aurait rien à lui dire.
    ///
    /// L'ORDRE DES DEUX CONTRÔLES N'EST PAS INDIFFÉRENT.
    ///
    /// La propriété se vérifie D'ABORD. L'inverse transformerait la route en oracle
    /// d'existence : un membre sans la capacité apprendrait, au 403 plutôt qu'au
    /// 404, que le lieu visé appartient bien à son propre vendeur — information
    /// qu'il n'a pas à obtenir sur un identifiant deviné.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult?> DenyUnlessOwnerAsync(
        Guid locationId,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        IInventoryModuleApi inventory,
        string capacite,
        CancellationToken ct)
    {
        if (IsAdmin(user))
        {
            return null;
        }

        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            // Authentifié mais sans rattachement commerçant : ce compte n'a aucun stock.
            return Results.NotFound();
        }

        var location = await inventory.GetLocationAsync(locationId, ct);

        // `OwnerId is null` EST UN REFUS, PAS UN LAISSER-PASSER. Un lieu sans
        // propriétaire appartient à la plateforme ; le comparer à `SellerId`
        // rendrait faux, mais l'écrire explicitement évite qu'une future
        // simplification en fasse un `true`.
        if (location?.OwnerId is not { } owner || owner != acces.SellerId)
        {
            return Results.NotFound();
        }

        // LE PROPRIÉTAIRE PORTE TOUT, ET `MerchantAccess.Can` LE SAIT DÉJÀ.
        //
        // `MerchantAccessApi` remplit `Permissions` avec le catalogue entier quand
        // `IsOwner`. Écrire ici `if (!acces.IsOwner && !acces.Can(...))` ajouterait
        // une seconde règle de contournement, dans un fichier qui n'a pas à savoir
        // ce qu'est un propriétaire — et le jour où la première changerait, celle-ci
        // resterait.
        if (!acces.Can(capacite))
        {
            return ApiResults.MissingCapability(capacite);
        }

        // AUCUNE CAPACITÉ DE STOCK N'EST CRITIQUE AUJOURD'HUI, ET LA LIGNE RESTE.
        //
        // Une recherche dans un ensemble. L'omettre rendrait cette garde subtilement
        // différente des quatre autres, et une future promotion au rang Critique
        // s'appliquerait partout sauf ici — silencieusement.
        if (MerchantCapabilities.RequiresStepUp(capacite) && !user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(capacite);
        }

        return null;
    }

    /// <summary>Même contrôle, à partir d'un ARTICLE : on remonte à son lieu.</summary>
    private static async Task<IResult?> DenyUnlessOwnerOfItemAsync(
        Guid itemId,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        IInventoryModuleApi inventory,
        ISender sender,
        string capacite,
        CancellationToken ct)
    {
        if (IsAdmin(user))
        {
            return null;
        }

        var item = await sender.Send(new GetInventoryItemQuery(itemId), ct);
        if (item.IsFailure || item.Value is null)
        {
            return Results.NotFound();
        }

        return await DenyUnlessOwnerAsync(item.Value.LocationId, user, access, inventory, capacite, ct);
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static async Task<IResult> ListLocationsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllFulfillmentLocationsQuery(), ct)).Match(items => Results.Ok(items));

    /// <summary>Les lieux d'expédition d'un propriétaire.</summary>
    /// <remarks>
    /// `ownerId` ÉTAIT PRIS TEL QUEL (tâche #229). N'importe quel compte
    /// authentifié listait les entrepôts d'un concurrent : commune, quartier, point
    /// de repère, téléphone de contact. C'est-à-dire l'adresse physique de son
    /// stock — la donnée qu'on protège le moins et qui se monnaie le mieux.
    ///
    /// ON COMPARE, ON NE FILTRE PAS. Contrairement aux deux lectures d'articles
    /// ci-dessous, la question est ici sans ambiguïté : soit c'est votre dossier,
    /// soit ce n'en est pas un. Rendre une liste vide brouillerait « vous n'avez
    /// pas de lieu » et « ce n'est pas vous ».
    /// </remarks>
    private static async Task<IResult> ListOwnerLocationsAsync(
        Guid ownerId, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
    {
        if (!IsAdmin(user))
        {
            // 403 ET NON 404 : `ownerId` EST UN IDENTIFIANT DE VENDEUR, VENU DE
            // L'URL, ET IL EST PUBLIC — il circule dans les liens de boutique. Le
            // cacher ne protégeait rien et rendait le diagnostic impossible au
            // membre légitime qui s'était trompé. Règle du dépôt : identifiant de
            // VENDEUR → 403 explicite, identifiant de RESSOURCE → 404.
            if (await AccesVendeurAsync(user, access, ct) is not { } acces || acces.SellerId != ownerId)
            {
                return ApiResults.Failure(
                    ErrorCodes.Forbidden,
                    "Ce dossier vendeur n'est pas le vôtre.",
                    StatusCodes.Status403Forbidden);
            }

            if (!acces.Can(MerchantCapabilities.StockLocationView))
            {
                return ApiResults.MissingCapability(MerchantCapabilities.StockLocationView);
            }
        }

        return (await sender.Send(new ListFulfillmentLocationsQuery(ownerId), ct))
            .Match(items => Results.Ok(items));
    }

    /// <remarks>
    /// `OwnerId` DU CORPS EST IGNORÉ POUR UN VENDEUR, ET REMPLACÉ.
    ///
    /// Le laisser passer permettrait de créer un lieu AU NOM D'UN AUTRE vendeur —
    /// puis d'y écrire du stock en toute légitimité, puisque la garde ne
    /// vérifierait plus qu'une propriété qu'on vient de s'attribuer. Le champ
    /// reste dans le contrat pour l'administration, qui crée les entrepôts de
    /// plateforme (`OwnerId = null`) et ceux d'un vendeur donné.
    /// </remarks>
    private static async Task<IResult> CreateLocationAsync(
        LocationRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
    {
        var owner = request.OwnerId;

        if (!IsAdmin(user))
        {
            if (await AccesVendeurAsync(user, access, ct) is not { } acces)
            {
                return Results.NotFound();
            }

            // LA CAPACITÉ SE CONTRÔLE ICI ET NON DANS `DenyUnlessOwnerAsync` :
            // le lieu n'existe pas encore, il n'y a aucune propriété à comparer.
            if (!acces.Can(MerchantCapabilities.StockLocationManage))
            {
                return ApiResults.MissingCapability(MerchantCapabilities.StockLocationManage);
            }

            owner = acces.SellerId;
        }

        return (await sender.Send(new CreateFulfillmentLocationCommand(
            request.Type,
            owner,
            request.Commune,
            request.Quartier,
            request.Landmark,
            request.Line,
            request.Latitude,
            request.Longitude,
            request.ContactPhone), ct))
            .Match(id => Results.Created($"/api/inventory/locations/{id}", new { id }));
    }

    private static async Task<IResult> UpdateLocationAsync(
        Guid id, LocationAddressRequest request, ClaimsPrincipal user,
        IMerchantAccessApi access, IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, inventory, MerchantCapabilities.StockLocationManage, ct)
        ?? (await sender.Send(new UpdateLocationAddressCommand(
            id,
            request.Commune,
            request.Quartier,
            request.Landmark,
            request.Line,
            request.Latitude,
            request.Longitude,
            request.ContactPhone), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteLocationAsync(
        Guid id, Guid? ownerId, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, inventory, MerchantCapabilities.StockLocationManage, ct)
        ?? (await sender.Send(new DeleteFulfillmentLocationCommand(id, ownerId), ct)).Match(() => Results.NoContent());

    /// <summary>Un article de stock. Gardé par appartenance depuis #229.</summary>
    private static async Task<IResult> GetItemAsync(Guid id, ClaimsPrincipal user, IMerchantAccessApi access, IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerOfItemAsync(id, user, access, inventory, sender, MerchantCapabilities.InventoryView, ct)
        ?? (await sender.Send(new GetInventoryItemQuery(id), ct)).Match(item => Results.Ok(item));

    /// <summary>Les lignes de stock d'une référence.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ICI ON FILTRE, ON NE REFUSE PAS — ET LA DIFFÉRENCE EST RAISONNÉE.
    ///
    /// Un SKU n'appartient pas à inventory-service : c'est catalog-service qui sait
    /// à quel produit, donc à quel vendeur, il se rattache. Décider « ce SKU n'est
    /// pas le vôtre » exigerait un appel inter-services à chaque lecture, pour une
    /// question à laquelle le stock lui-même répond : les lignes portent un
    /// `LocationId`, et un lieu porte un propriétaire.
    ///
    /// On rend donc les lignes du CALLER, et elles seules. Un vendeur qui interroge
    /// sa propre référence voit exactement ce qu'il voyait avant ; celui qui
    /// interroge celle d'un concurrent reçoit une liste vide.
    ///
    /// CE QUI FUYAIT : les SKU sont PUBLICS — `OfferSummary.Sku` est rendu par la
    /// Buy Box à qui consulte une fiche produit. Il suffisait donc de lire une page
    /// de vitrine pour obtenir, entrepôt par entrepôt, le stock disponible d'un
    /// concurrent. C'est la donnée sur laquelle on décide de casser un prix.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult> ListBySkuAsync(
        string sku, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
    {
        var resultat = await sender.Send(new ListInventoryBySkuQuery(sku), ct);
        if (resultat.IsFailure)
        {
            return resultat.Match(items => Results.Ok(items));
        }

        if (IsAdmin(user))
        {
            return Results.Ok(resultat.Value);
        }

        var miens = await MesLieuxAsync(user, access, sender, ct);
        return Results.Ok(resultat.Value.Where(i => miens.Contains(i.LocationId)).ToList());
    }

    /// <summary>Le stock de plusieurs lieux, en un appel.</summary>
    /// <remarks>
    /// LES LIEUX DEMANDÉS SONT RÉDUITS À CEUX QU'ON POSSÈDE, avant la requête.
    ///
    /// Filtrer APRÈS aurait suffi à ne rien fuir, mais aurait laissé un appelant
    /// mesurer le temps de réponse sur mille identifiants d'autrui. Réduire d'abord
    /// rend la route inutilisable comme sonde.
    /// </remarks>
    private static async Task<IResult> ListByLocationsAsync(
        LocationIdsRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
    {
        var demandes = request.LocationIds;

        if (!IsAdmin(user))
        {
            var miens = await MesLieuxAsync(user, access, sender, ct);
            demandes = demandes.Where(miens.Contains).ToList();

            // COURT-CIRCUIT : sans lui, une liste vide partirait vers un `IN ()`
            // que PostgreSQL refuse — et l'appelant recevrait une erreur serveur
            // là où la bonne réponse est « rien ne vous appartient là-dedans ».
            if (demandes.Count == 0)
            {
                return Results.Ok(Array.Empty<InventoryItemSummary>());
            }
        }

        return (await sender.Send(new ListInventoryByLocationsQuery(demandes), ct))
            .Match(items => Results.Ok(items));
    }

    /// <summary>Les identifiants des lieux du porteur du jeton.</summary>
    /// <remarks>
    /// UNE REQUÊTE, PAS UNE PAR LIGNE. Vérifier l'appartenance lieu par lieu
    /// ferait un aller-retour par ligne de stock — sur un vendeur à cinquante
    /// références, cinquante lectures pour un filtre.
    /// </remarks>
    private static async Task<HashSet<Guid>> MesLieuxAsync(
        ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════
        // LA CAPACITÉ EST EXIGÉE ICI, ET SON ABSENCE ÉTAIT UN CONTOURNEMENT.
        //
        // `GET /items/sku/{sku}` et `POST /items/by-locations` filtraient bien sur
        // les lieux du vendeur — rien ne fuyait vers l'extérieur — mais n'exigeaient
        // AUCUNE capacité, alors que leurs voisines exigent `INVENTORY_VIEW`.
        //
        // Un chargé de clientèle recevait donc 403 sur `GET /items/{id}` et obtenait
        // les mêmes quantités, entrepôt par entrepôt, par l'une de ces deux routes.
        // La séparation que `INVENTORY_VIEW` établit tenait sur une route et se
        // contournait sur deux autres du même service.
        //
        // ELLE REND UN ENSEMBLE VIDE PLUTÔT QU'UN REFUS, ET C'EST VOULU.
        //
        // Ces deux routes servent aussi la fiche produit et le panier CLIENT, pour
        // des SKU qui ne sont pas ceux de l'appelant. Un 403 y casserait la
        // vitrine ; l'ensemble vide produit exactement ce que le filtre produisait
        // déjà pour un acheteur — « rien ne vous appartient là-dedans ».
        // ═════════════════════════════════════════════════════════════════════
        if (await AccesVendeurAsync(user, access, ct) is not { } acces
            || !acces.Can(MerchantCapabilities.InventoryView))
        {
            return [];
        }

        var lieux = await sender.Send(new ListFulfillmentLocationsQuery(acces.SellerId), ct);
        return lieux.IsFailure ? [] : lieux.Value.Select(l => l.Id).ToHashSet();
    }

    /// <summary>Disponibilité totale d'une référence.</summary>
    /// <remarks>
    /// CELLE-CI RESTE OUVERTE À TOUT AUTHENTIFIÉ, ET C'EST UN CHOIX EXPLICITE.
    ///
    /// `AvailabilitySummary` ne porte que `Sku` et `TotalAvailable` : aucun lieu,
    /// aucun propriétaire, aucune répartition. C'est la question que pose une fiche
    /// produit côté ACHETEUR — « peut-on encore l'acheter » — et la fermer casserait
    /// l'application client sans rien protéger de plus.
    ///
    /// La distinction avec `ListBySkuAsync` tient entièrement à la GRANULARITÉ : un
    /// total ne dit pas où se trouve la marchandise ni combien il en reste par
    /// entrepôt. Si un jour ce contrat gagne une répartition, cette route devra
    /// rejoindre les autres.
    /// </remarks>
    private static async Task<IResult> AvailabilityAsync(string sku, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetAvailabilityQuery(sku), ct)).Match(item => Results.Ok(item));

    /// <remarks>
    /// `take` EST UN SOUHAIT, PAS UN ORDRE : il est plafonné par
    /// `ListLowStockQueryHandler`. Cette liste chargeait auparavant TOUTE la table
    /// de stock avec toutes ses réservations (§12) ; laisser le client rouvrir ce
    /// balayage par un `take` géant reviendrait à ne pas l'avoir fermé.
    /// </remarks>
    private static async Task<IResult> LowStockAsync(int? take, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListLowStockQuery(take ?? 50), ct)).Match(items => Results.Ok(items));

    /// <remarks>
    /// La garde porte sur `request.LocationId` : créer un article, c'est poser du
    /// stock DANS un lieu, et c'est le lieu qui a un propriétaire.
    /// </remarks>
    private static async Task<IResult> CreateItemAsync(
        CreateInventoryItemRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(request.LocationId, user, access, inventory, MerchantCapabilities.InventoryAdjust, ct)
        ?? (await sender.Send(new CreateInventoryItemCommand(
            request.Sku, request.LocationId, request.OnHand, request.ReorderThreshold), ct))
            .Match(id => Results.Created($"/api/inventory/items/{id}", new { id }));

    private static async Task<IResult> ReceiveStockAsync(
        Guid id, QuantityRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerOfItemAsync(id, user, access, inventory, sender, MerchantCapabilities.InventoryAdjust, ct)
        ?? (await sender.Send(new ReceiveStockCommand(
               id, request.Quantity, CurrentUserId(user), request.Reason), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> AdjustStockAsync(
        Guid id, DeltaRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerOfItemAsync(id, user, access, inventory, sender, MerchantCapabilities.InventoryAdjust, ct)
        ?? (await sender.Send(new AdjustStockCommand(
               id, request.Delta, CurrentUserId(user), request.Reason), ct))
            .Match(() => Results.NoContent());

    /// <summary>
    /// Le journal des mouvements d'un article — qui, quand, combien, pourquoi.
    /// </summary>
    private static async Task<IResult> ListMovementsAsync(
        Guid id, int? take, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerOfItemAsync(
               id, user, access, inventory, sender, MerchantCapabilities.StockMovementView, ct)
        ?? (await sender.Send(new ListStockMovementsQuery(id, take ?? 50), ct))
            .Match(Results.Ok);

    /// <summary>
    /// Déplace du stock d'un lieu vers un autre.
    /// </summary>
    /// <remarks>
    /// LES DEUX GARDES SONT SÉQUENTIELLES, ET LA SOURCE PASSE EN PREMIER.
    ///
    /// Si l'appelant ne possède pas la source, il obtient 404 sans qu'on ait rien
    /// révélé de la destination. L'ordre inverse dirait, à qui tâtonne, qu'un
    /// article de destination existe — avant même d'avoir établi qu'il a quoi que
    /// ce soit à transférer.
    ///
    /// L'ACTEUR VIENT DU JETON, JAMAIS DU CORPS. C'est le §36 : un identifiant
    /// fourni par l'appelant ne constitue pas une preuve. Un `ActorUserId` reçu
    /// dans la requête permettrait d'attribuer sa propre casse à un collègue.
    /// </remarks>
    private static async Task<IResult> TransferStockAsync(
        TransferRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerOfItemAsync(
               request.SourceItemId, user, access, inventory, sender,
               MerchantCapabilities.InventoryTransfer, ct)
        ?? await DenyUnlessOwnerOfItemAsync(
               request.DestinationItemId, user, access, inventory, sender,
               MerchantCapabilities.InventoryTransfer, ct)
        ?? (await sender.Send(new TransferStockCommand(
               request.SourceItemId, request.DestinationItemId, request.Quantity,
               CurrentUserId(user), request.Reason), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> SetThresholdAsync(
        Guid id, ThresholdRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IInventoryModuleApi inventory, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerOfItemAsync(id, user, access, inventory, sender, MerchantCapabilities.InventoryAdjust, ct)
        ?? (await sender.Send(new SetReorderThresholdCommand(id, request.Threshold), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ReserveAsync(ReservationRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReserveStockCommand(
            request.Sku, request.LocationId, request.OrderId, request.Quantity, request.ExpiresInMinutes), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ReleaseAsync(ReservationKeyRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReleaseReservationCommand(request.Sku, request.LocationId, request.OrderId), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ConfirmAsync(ReservationKeyRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmReservationCommand(request.Sku, request.LocationId, request.OrderId), ct))
            .Match(() => Results.NoContent());

    public sealed record LocationRequest(
        string Type,
        Guid? OwnerId,
        string? Commune,
        string? Quartier,
        string? Landmark,
        string? Line,
        double? Latitude,
        double? Longitude,
        string? ContactPhone);

    public sealed record LocationAddressRequest(
        string? Commune,
        string? Quartier,
        string? Landmark,
        string? Line,
        double? Latitude,
        double? Longitude,
        string? ContactPhone);

    public sealed record LocationIdsRequest(IReadOnlyCollection<Guid> LocationIds);

    public sealed record CreateInventoryItemRequest(string Sku, Guid LocationId, int OnHand, int ReorderThreshold);

    /// <summary>
    /// `Reason` EST OPTIONNEL SUR UNE RÉCEPTION, OBLIGATOIRE NULLE PART.
    ///
    /// L'exiger sur une réception ferait saisir « livraison » mille fois pour rien.
    /// Sur un AJUSTEMENT il est le seul intérêt du geste — mais l'imposer ici
    /// casserait les appelants existants, et un motif arraché par un formulaire
    /// vaut « ras » dans 90 % des cas. La colonne est nullable ; l'interface est
    /// l'endroit où insister.
    /// </summary>
    public sealed record QuantityRequest(int Quantity, string? Reason = null);

    public sealed record DeltaRequest(int Delta, string? Reason = null);

    /// <summary>
    /// AUCUN `ActorUserId` DANS CE CORPS, ET C'EST DÉLIBÉRÉ. Il vient du jeton.
    /// Le laisser entrer par la requête permettrait d'attribuer sa propre casse à
    /// un collègue — sur la seule table qui dise qui a fait quoi au stock.
    /// </summary>
    public sealed record TransferRequest(
        Guid SourceItemId, Guid DestinationItemId, int Quantity, string? Reason = null);

    public sealed record ThresholdRequest(int Threshold);

    public sealed record ReservationRequest(string Sku, Guid LocationId, Guid OrderId, int Quantity, int ExpiresInMinutes = 15);

    public sealed record ReservationKeyRequest(string Sku, Guid LocationId, Guid OrderId);
}
