using HBA.Catalog.Application.Abstractions;
using HBA.Merchants.Contracts;
using HBA.Catalog.Contracts;
using HBA.Catalog.Application.Offers;
using System.Security.Claims;
using HBA.Catalog.Application.Brands.Commands.CreateBrand;
using HBA.Catalog.Application.Brands.Commands.DeleteBrand;
using HBA.Catalog.Application.Brands.Commands.PublishBrand;
using HBA.Catalog.Application.Brands.Commands.UnpublishBrand;
using HBA.Catalog.Application.Brands.Commands.UpdateBrand;
using HBA.Catalog.Application.Brands.Queries.GetBrand;
using HBA.Catalog.Application.Brands.Queries.ListBrands;
using HBA.Catalog.Application.Categories.Commands.CreateCategory;
using HBA.Catalog.Application.Categories.Commands.DeleteCategory;
using HBA.Catalog.Application.Categories.Commands.PublishCategory;
using HBA.Catalog.Application.Categories.Commands.UnpublishCategory;
using HBA.Catalog.Application.Categories.Commands.UpdateCategory;
using HBA.Catalog.Application.Categories.Queries.GetCategory;
using HBA.Catalog.Application.Categories.Queries.ListCategories;
using HBA.Catalog.Application.Products;
using HBA.Catalog.Application.Products.Commands.AddProductMedia;
using HBA.Catalog.Application.Products.Commands.AddProductVariant;
using HBA.Catalog.Application.Products.Commands.ChangeProductStatus;
using HBA.Catalog.Application.Products.Commands.CreateProduct;
using HBA.Catalog.Application.Products.Commands.DeleteProduct;
using HBA.Catalog.Application.Products.Commands.RemoveProductMedia;
using HBA.Catalog.Application.Products.Commands.RemoveProductVariant;
using HBA.Catalog.Application.Products.Commands.SetVariantActive;
using HBA.Catalog.Application.Products.Commands.ReorderProductMedia;
using HBA.Catalog.Application.Products.Commands.SetPrimaryProductMedia;
using HBA.Catalog.Application.Products.Commands.SetProductTags;
using HBA.Catalog.Application.Products.Commands.UpdateProduct;
using HBA.Catalog.Application.Products.Commands.UpdateProductVariant;
using HBA.Catalog.Application.Products.Queries.GetProduct;
using HBA.Catalog.Application.Products.Queries.PublicCatalog;
using HBA.Catalog.Application.Reviews;
using HBA.Catalog.Application.Attributes;
using HBA.Catalog.Application.Brands;
using HBA.Catalog.Application.Products.Queries.ListAllProducts;
using HBA.Catalog.Application.Products.Queries.ListProductsBySeller;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Catalog.Api.Endpoints;

/// <summary>Surface HTTP initiale du service Catalog.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════════════════════════════════════════════
        // LA RACINE PASSE DE `/api/catalog` À `/api/v1/catalog` (§22).
        //
        // Cinquième service à s'aligner, après identity, media, promotions et
        // users. Ce n'est pas cosmétique : sans version dans l'URL, la seule façon
        // de faire évoluer une réponse de manière cassante est de casser les
        // clients installés — et une application mobile ne se met pas à jour à la
        // demande.
        //
        // CE CHANGEMENT SERAIT UNE PANNE TOTALE SANS SON PENDANT À LA PASSERELLE.
        //
        // Toutes les applications déjà installées appellent `/api/catalog/...`.
        // Renommer ici et s'arrêter là leur rendrait 404 sur TOUTE la vitrine, à
        // la seconde du déploiement. La coquille de dépréciation ajoutée dans
        // `appsettings.json` de la passerelle réécrit l'ancien chemin vers le
        // nouveau — même recette que pour user-service, et elle se retire quand la
        // télémétrie montre que plus personne n'appelle l'ancien.
        //
        // Les deux vont ensemble. Livrer l'un sans l'autre est le défaut.
        // ═════════════════════════════════════════════════════════════════════
        var publicCatalog = app.MapGroup("/api/v1/catalog").WithTags("Catalog · Public");
        publicCatalog.MapGet("/brands", ListBrandsAsync).AllowAnonymous();
        publicCatalog.MapGet("/brands/{id:guid}", GetBrandAsync).AllowAnonymous();
        publicCatalog.MapGet("/categories", ListCategoriesAsync).AllowAnonymous();
        publicCatalog.MapGet("/categories/{id:guid}", GetCategoryAsync).AllowAnonymous();

        // LE SCHÉMA D'UNE CATÉGORIE EST PUBLIC, ET CE N'EST PAS UNE FUITE.
        //
        // C'est ce que le formulaire vendeur consomme pour construire ses champs
        // dynamiques (§13, étape 8) — et ce dont la vitrine a besoin pour proposer
        // ses filtres (§17). Il ne contient que du référentiel : des libellés, des
        // types et des listes de valeurs, les mêmes pour tout le monde.
        publicCatalog.MapGet("/categories/{id:guid}/attributes", GetCategoryAttributesAsync).AllowAnonymous();
        // ═════════════════════════════════════════════════════════════════════
        // CES QUATRE ROUTES SERVAIENT DU CONTENU NON VALIDÉ. C'EST FERMÉ ICI.
        //
        // Elles appelaient les requêtes du back-office — `ListAllProductsQuery`
        // est documentée « console admin » — qui projettent la révision COURANTE
        // et n'appliquent de filtre de statut que si on leur en passe un. Sans
        // paramètre, la vitrine anonyme rendait les brouillons, les fiches en
        // attente de validation, les rejetées et les suspendues ; pour une fiche
        // publiée, elle rendait la version en cours de relecture.
        //
        // Elles pointent désormais les requêtes de `Queries/PublicCatalog/`, qui
        // n'ont AUCUN paramètre capable d'élargir ce qu'elles montrent.
        //
        // L'ORDRE DES DEUX ROUTES `/products/{…}` COMPTE PEU, LA CONTRAINTE SI.
        //
        // `{id:guid}` est plus spécifique que `{slug}` : le routeur d'ASP.NET
        // choisit la contrainte satisfaite. Sans le `:guid`, un identifiant serait
        // interprété comme un slug et rendrait 404.
        // ═════════════════════════════════════════════════════════════════════
        publicCatalog.MapGet("/products", SearchPublicProductsAsync).AllowAnonymous();
        publicCatalog.MapGet("/products/{id:guid}", GetPublicProductAsync).AllowAnonymous();
        publicCatalog.MapGet("/products/{slug}", GetPublicProductBySlugAsync).AllowAnonymous();
        publicCatalog.MapGet("/sellers/{sellerId:guid}/products", ListPublicSellerProductsAsync).AllowAnonymous();

        // ═════════════════════════════════════════════════════════════════════
        // LE SEGMENT `/admin` N'A JAMAIS ÉTÉ UNE POLITIQUE.
        //
        // Le groupe n'exigeait qu'un jeton — celui que délivre n'importe quelle
        // inscription acheteur. Le référentiel de la place de marché était donc
        // ouvert en écriture à tout le monde : créer une marque, renommer une
        // catégorie, et surtout SUPPRIMER l'une ou l'autre.
        //
        // Une catégorie supprimée n'emporte pas que sa ligne : elle emporte le
        // rattachement de tous les produits qui la référencent, chez tous les
        // vendeurs, d'un seul appel. Dépublier suffit à faire disparaître une
        // famille entière de la vitrine.
        //
        // Le référentiel est de la gouvernance. Il rejoint le groupe qui le dit.
        // ═════════════════════════════════════════════════════════════════════
        var admin = app.MapAdminGroup("/api/v1/catalog/admin").WithTags("Catalog · Admin");
        admin.MapPost("/brands", CreateBrandAsync).AllowIdempotency();
        admin.MapPut("/brands/{id:guid}", UpdateBrandAsync);
        admin.MapPost("/brands/{id:guid}/publish", PublishBrandAsync);
        admin.MapPost("/brands/{id:guid}/unpublish", UnpublishBrandAsync);
        admin.MapDelete("/brands/{id:guid}", DeleteBrandAsync);
        admin.MapPost("/categories", CreateCategoryAsync).AllowIdempotency();
        admin.MapPut("/categories/{id:guid}", UpdateCategoryAsync);
        admin.MapPost("/categories/{id:guid}/publish", PublishCategoryAsync);
        admin.MapPost("/categories/{id:guid}/unpublish", UnpublishCategoryAsync);
        admin.MapDelete("/categories/{id:guid}", DeleteCategoryAsync);

        // LA VUE DE GOUVERNANCE REVIENT ICI, D'OÙ ELLE N'AURAIT JAMAIS DÛ SORTIR.
        //
        // `ListAllProductsQuery` rend TOUS les statuts et la répartition du
        // catalogue par statut. C'est ce qu'un administrateur doit voir, et ce
        // qu'un visiteur ne doit pas. Elle était branchée sur la route publique.
        admin.MapGet("/products", ListAllProductsAsync);
        admin.MapGet("/products/{id:guid}", GetProductForAdminAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LA VALIDATION (§16) — SIX ROUTES QUI N'EXISTAIENT PAS.
        //
        // SANS ELLES, UNE FICHE SOUMISE NE POUVAIT JAMAIS ÊTRE APPROUVÉE.
        //
        // `Product.Approve`, `Reject`, `Suspend` et `Restore` existaient depuis le
        // lot 1, testés, appelés par personne. Le parcours du §28 s'arrêtait à
        // l'étape 4, et `ChangeProductStatusCommandHandler` renvoyait le vendeur
        // vers « l'API admin » — c'est-à-dire vers rien.
        //
        // `/products/reviews` AVANT `/products/{id:guid}` NE CHANGE RIEN ICI :
        // « reviews » n'est pas un GUID, la contrainte tranche. Mais l'ordre est
        // conservé lisible parce qu'un futur `{id}` sans contrainte inverserait
        // le résultat sans qu'aucun test ne s'en aperçoive.
        // ═════════════════════════════════════════════════════════════════════
        admin.MapGet("/products/reviews", ListPendingReviewsAsync);
        admin.MapGet("/products/{id:guid}/review", GetProductReviewsAsync);
        admin.MapPost("/products/{id:guid}/approve", ApproveProductAsync);
        admin.MapPost("/products/{id:guid}/reject", RejectProductAsync);
        admin.MapPost("/products/{id:guid}/suspend", SuspendProductAsync);
        admin.MapPost("/products/{id:guid}/restore", RestoreProductAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LE RÉFÉRENTIEL D'ATTRIBUTS ET LES DEMANDES DE MARQUE (§10).
        //
        // CÔTÉ ADMINISTRATION SEULEMENT — C'EST TOUT L'INTÉRÊT DU §10.
        //
        // « Le vendeur ne crée pas directement une nouvelle marque officielle. »
        // Sans cette séparation, « Samsung », « SAMSUNG » et « samsumg »
        // cohabitent au bout d'un mois, et le filtre par marque de la vitrine
        // devient inutilisable. Même raison pour les attributs : trois codes
        // « couleur » donnent trois filtres au lieu d'un.
        // ═════════════════════════════════════════════════════════════════════
        admin.MapGet("/attributes", ListAttributeDefinitionsAsync);
        admin.MapPost("/attributes", CreateAttributeDefinitionAsync).AllowIdempotency();
        admin.MapPost("/categories/{id:guid}/attributes", AssignAttributeToCategoryAsync);
        admin.MapDelete("/categories/{id:guid}/attributes/{attributeId:guid}", RemoveAttributeFromCategoryAsync);

        admin.MapGet("/brands/requests", ListPendingBrandRequestsAsync);
        admin.MapPost("/brands/requests/{id:guid}/approve", ApproveBrandRequestAsync);
        admin.MapPost("/brands/requests/{id:guid}/reject", RejectBrandRequestAsync);

        // ═════════════════════════════════════════════════════════════════════
        // L'IDOR SUR LES PRODUITS EST FERMÉ. CE BLOC DISAIT LE CONTRAIRE.
        //
        // CORRECTION D'UN COMMENTAIRE PÉRIMÉ, ET C'EST UN DÉFAUT EN SOI.
        //
        // Il annonçait « le défaut reste réel sur les DOUZE routes produit
        // ci-dessous » et « `CreateProductAsync` prend toujours `SellerId` DANS LE
        // CORPS ». Les deux affirmations sont fausses depuis que
        // `DenyUnlessProductOwnerAsync` garde les douze routes et que le vendeur
        // est résolu depuis le jeton.
        //
        // Un commentaire qui décrit une faille refermée coûte deux fois : on
        // rouvre le dossier pour rien, et le jour où il en reste une vraie, plus
        // personne ne croit les bandeaux.
        //
        // LE RÔLE EST POSÉ. C'EST CE QUE LE BANDEAU PRÉCÉDENT ANNONÇAIT.
        //
        // Ce groupe n'exigeait qu'un JETON : tout compte authentifié — acheteur
        // compris — entrait dans la surface vendeur, et seule la garde
        // d'appartenance, route par route, l'arrêtait en rendant 404. Cela tenait
        // tant que CHAQUE route portait sa garde, c'est-à-dire tant que personne
        // n'en ajoutait une en l'oubliant. La protection était une discipline, pas
        // une barrière.
        //
        // `MapSellerGroup` exige `Seller`, `Admin` ou `Moderator` — les deux
        // derniers parce que `DenyUnlessProductOwnerAsync` les laisse déjà passer
        // délibérément, pour qu'un modérateur puisse corriger la fiche d'un vendeur
        // injoignable. Les exclure ici aurait fermé au niveau du groupe un chemin
        // que le handler ouvre trois lignes plus bas.
        // ═════════════════════════════════════════════════════════════════════
        var seller = app.MapSellerGroup("/api/v1/catalog/seller").WithTags("Catalog · Seller");

        // SANS CES DEUX ROUTES, LE VENDEUR N'A PLUS ACCÈS À SES BROUILLONS.
        //
        // Il les lisait par la vitrine anonyme, qui rendait tous les statuts. En
        // fermant la fuite, on lui retirait le seul chemin vers ses propres fiches
        // non publiées — corriger l'un sans l'autre aurait remplacé un défaut de
        // confidentialité par une régression fonctionnelle.
        //
        // La vue vendeur montre la révision COURANTE : c'est celle qu'il édite.
        seller.MapGet("/products", ListMyProductsAsync);
        seller.MapGet("/products/{id:guid}", GetMyProductAsync);

        // La seule route de marque ouverte au vendeur : DEMANDER, pas créer (§10).
        seller.MapPost("/brands/requests", RequestBrandCreationAsync).AllowIdempotency();

        // ═════════════════════════════════════════════════════════════════════
        // IDEMPOTENCE DES CRÉATIONS (§25) — `Allow`, ET NON `Require`.
        //
        // LE CHOIX EST DÉLIBÉRÉ, ET IL SE RELIT COMME UNE DETTE ASSUMÉE.
        //
        // `RequireIdempotency()` rend l'en-tête `Idempotency-Key` OBLIGATOIRE :
        // toute requête qui ne le porte pas est refusée en 400. Posé aujourd'hui,
        // il casserait à la seconde du déploiement chaque appel de création des
        // applications déjà installées — aucune ne l'envoie.
        //
        // Ce qu'on échange contre cela : un double POST peut créer deux fiches. Le
        // dommage est visible et réparable — le vendeur voit deux brouillons et en
        // supprime un. Aucune de ces routes ne débite, ne crédite ni ne commande.
        //
        // `AllowIdempotency()` honore la clé quand elle est fournie : les clients
        // peuvent migrer un par un. Le jour où la télémétrie montre que tous
        // l'envoient, ces lignes deviennent `RequireIdempotency()` et c'est un
        // changement d'une ligne par route.
        //
        // CE N'EST PAS UN NO-OP EN ATTENDANT. Un client qui envoie la clé est
        // protégé dès maintenant, y compris contre le retry automatique de sa
        // propre couche réseau — c'est-à-dire contre le cas le plus fréquent.
        // ═════════════════════════════════════════════════════════════════════
        seller.MapPost("/products", CreateProductAsync).AllowIdempotency();
        seller.MapPut("/products/{id:guid}", UpdateProductAsync);
        seller.MapPost("/products/{id:guid}/status", ChangeStatusAsync);
        seller.MapDelete("/products/{id:guid}", DeleteProductAsync);
        seller.MapPut("/products/{id:guid}/tags", SetTagsAsync);
        seller.MapPost("/products/{id:guid}/variants", AddVariantAsync).AllowIdempotency();
        seller.MapPut("/products/{id:guid}/variants/{variantId:guid}", UpdateVariantAsync);
        seller.MapDelete("/products/{id:guid}/variants/{variantId:guid}", RemoveVariantAsync);

        // RETIRER DE LA VENTE N'EST PAS SUPPRIMER (tâche #230). `DELETE` efface la
        // ligne et laisse un historique de commandes qui pointe vers rien ; ceci
        // ferme la vente en gardant la déclinaison, ses attributs et son SKU.
        seller.MapPost("/products/{id:guid}/variants/{variantId:guid}/status", SetVariantActiveAsync);
        seller.MapPost("/products/{id:guid}/media", AddMediaAsync).AllowIdempotency();
        seller.MapDelete("/products/{id:guid}/media/{mediaId:guid}", RemoveMediaAsync);
        seller.MapPost("/products/{id:guid}/media/{mediaId:guid}/primary", SetPrimaryMediaAsync);
        seller.MapPut("/products/{id:guid}/media/order", ReorderMediaAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LES OFFRES — LE PRIX, ENFIN JOIGNABLE PAR HTTP (phase 3.5).
        //
        // TOUTES GARDÉES PAR `DenyUnlessOwnerAsync`, contrairement aux routes
        // produit du dessus. La différence n'est pas d'exigence mais de
        // faisabilité : une offre PORTE son `SellerId`, il n'y a donc rien à
        // remonter. Un produit ne porte que celui de sa fiche, et la chaîne est
        // la même — c'est pourquoi ce gabarit ferme aussi #179 le jour venu.
        //
        // DEUX COMMANDES NE SONT PAS EXPOSÉES ICI, ET C'EST DÉLIBÉRÉ :
        //
        //   • `MarkOfferOutOfStockCommand` — c'est le STOCK qui décide, pas le
        //     vendeur. Elle appartient à Inventory, par événement.
        //   • `SuspendOfferCommand` — c'est une sanction de plateforme. Un
        //     vendeur qui pourrait la lever annulerait sa propre suspension.
        //
        // Les exposer ici les mettrait à portée de celui qu'elles visent.
        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        // DÉTOURAGE D'UNE PHOTO PRODUIT — LA ROUTE QUI MANQUAIT.
        //
        // `IImageProcessor` EXISTAIT, AVEC TROIS IMPLÉMENTATIONS, ET AUCUN
        //    APPELANT.
        //
        // `RembgImageProcessor`, `CloudinaryImageProcessor`, `NullImageProcessor`,
        // le drapeau `IImageProcessingAvailability`, l'enregistrement conditionnel
        // dans `CatalogModuleInstaller` : tout était écrit. Il n'y avait
        // simplement PAS d'endpoint, donc l'application appelait un bouchon
        // `NotMigrated` et affichait « Le détourage a échoué ». Le message était
        // exact et la cause introuvable.
        //
        // ELLE REND L'IMAGE, PAS UNE URL. Le détourage précède le dépôt : à ce
        // stade la photo n'appartient à aucun produit, et rien ne doit être
        // stocké — un vendeur qui annule ne doit pas laisser d'objet derrière lui.
        //
        // MULTIPART, DONC `DisableAntiforgery`. Les minimal APIs exigent un
        // jeton antiforgery sur tout formulaire multipart depuis .NET 8 ; ce
        // client est une application mobile porteuse d'un jeton Bearer, pas un
        // navigateur avec des cookies — il n'y a pas de requête intersite à
        // contrefaire.
        // ═════════════════════════════════════════════════════════════════════
        seller.MapPost("/products/images/process", ProcessProductImageAsync)
            .DisableAntiforgery();

        seller.MapPost("/offers", CreateOfferAsync).AllowIdempotency();
        seller.MapGet("/stores/{storeId:guid}/offers", ListStoreOffersAsync);
        seller.MapPut("/offers/{id:guid}/price", ChangeOfferPriceAsync);
        seller.MapPut("/offers/{id:guid}/handling-time", SetOfferHandlingTimeAsync);
        seller.MapPut("/offers/{id:guid}/promotion", ApplyOfferPromotionAsync);
        seller.MapDelete("/offers/{id:guid}/promotion", RemoveOfferPromotionAsync);
        seller.MapPost("/offers/{id:guid}/activate", ActivateOfferAsync);
        seller.MapPost("/offers/{id:guid}/pause", PauseOfferAsync);
        seller.MapDelete("/offers/{id:guid}", ArchiveOfferAsync);

        return app;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LA GARDE DE PROPRIÉTÉ DES OFFRES.
    //
    // 404 ET NON 403, comme partout ailleurs : un 403 confirmerait à qui
    // tâtonne que l'offre existe.
    //
    // L'ADMINISTRATION PASSE OUTRE. Un modérateur doit pouvoir corriger le
    // prix aberrant d'un vendeur injoignable.
    // ═════════════════════════════════════════════════════════════════════════

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static bool IsAdmin(ClaimsPrincipal user)
        => user.IsInRole("Admin") || user.IsInRole("Moderator");

    /// <summary>
    /// Le contexte d'accès du porteur du jeton — vendeur, capacités, boutiques —
    /// ou <c>null</c> s'il n'a aucun rattachement commerçant.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE N'EST PLUS `GetSellerByUserIdAsync`, ET LA DIFFÉRENCE EST TOUT LE LOT.
    ///
    /// `GetSellerByUserIdAsync` ne résout QUE les propriétaires : elle lit la
    /// colonne `UserId` du dossier vendeur. Un gestionnaire de catalogue recruté
    /// par le vendeur n'a pas de dossier à son nom — cette méthode rendait `null`
    /// et les trente routes de ce groupe répondaient 404. L'écran « mes produits »
    /// fonctionnait pour le patron et pour personne d'autre.
    ///
    /// `GetAccessAsync` rend le même `SellerId` pour le propriétaire, ET le
    /// rattachement du membre, accompagné de ses permissions. La propriété seule
    /// ne suffit donc plus à autoriser : elle dit QUEL vendeur, la capacité dit SI
    /// l'on peut. Deux contrôles distincts, tous deux obligatoires.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<MerchantAccess?> AccesVendeurAsync(
        ClaimsPrincipal user, IMerchantAccessApi access, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? null
            : await access.GetAccessAsync(userId, ct);

    /// <summary>
    /// La capacité exigée par un changement de statut — elle DÉPEND du statut visé.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE SEULE ROUTE, TROIS PERMISSIONS DISTINCTES.
    ///
    /// `POST /products/{id}/status` porte trois gestes que le cahier sépare
    /// explicitement : soumettre à validation, publier, dépublier. Exiger une
    /// permission unique les confondrait — et c'est précisément la distinction qui
    /// intéresse un vendeur : un rédacteur prépare et soumet, seul un responsable
    /// met en vitrine.
    ///
    /// LE DÉFAUT EST `PRODUCT_UPDATE`, PAS UN REFUS.
    ///
    /// Les autres cibles (`Draft`, et tout ce que le domaine refusera ensuite) sont
    /// des retours en arrière sur un brouillon. Refuser ici sur un statut inconnu
    /// masquerait la vraie erreur — le handler rend déjà une validation propre pour
    /// un statut qu'il ne sait pas lire, et ce message-là est utile.
    ///
    /// LA NORMALISATION EST CELLE DU HANDLER, RECOPIÉE.
    ///
    /// `ChangeProductStatusCommandHandler` accepte `PENDING_REVIEW` comme
    /// `PendingReview`. Une garde qui ne comparerait qu'à la forme PascalCase
    /// laisserait `PUBLISHED` exiger `PRODUCT_UPDATE` au lieu de `PRODUCT_PUBLISH`
    /// — un contournement en une chaîne de caractères.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static string CapaciteDuStatut(string? statut)
        => (statut?.Replace("_", string.Empty).Trim().ToUpperInvariant()) switch
        {
            "PENDINGREVIEW" => MerchantCapabilities.ProductSubmitForReview,
            "PUBLISHED" => MerchantCapabilities.ProductPublish,
            "UNPUBLISHED" => MerchantCapabilities.ProductUnpublish,
            _ => MerchantCapabilities.ProductUpdate
        };

    /// <summary>Refuse si la fiche produit n'appartient pas à l'appelant.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA FAILLE QUE CETTE MÉTHODE FERME (tâche #179).
    ///
    /// Les douze routes produit du groupe vendeur ne vérifiaient RIEN d'autre que
    /// la présence d'un jeton. Or les identifiants de produits sont publics : la
    /// vitrine les rend à qui les demande, sans authentification. N'importe quel
    /// compte acheteur pouvait donc renommer un produit, le dépublier, le
    /// supprimer, réécrire ses déclinaisons ou effacer ses photos — il suffisait
    /// de s'inscrire et de lire une page de catalogue.
    ///
    /// 404, JAMAIS 403, ET C'EST LA MÊME RAISON QUE POUR LES OFFRES.
    ///
    /// Un 403 confirmerait que la fiche existe. Sur un identifiant deviné, cette
    /// confirmation est déjà une fuite : elle permet d'énumérer le catalogue des
    /// concurrents, y compris leurs brouillons non publiés.
    ///
    /// ELLE LIT PAR `ICatalogModuleApi`, DONC PAR LE CACHE.
    ///
    /// `GetProductAsync` est mis en cache. C'est acceptable ICI et nulle part
    /// ailleurs : le `SellerId` d'une fiche ne change JAMAIS après sa création —
    /// il n'existe aucune commande de transfert de propriété. Un cache sur une
    /// donnée immuable ne peut pas rendre une réponse périmée. Le jour où un
    /// transfert existerait, cette garde devrait relire le dépôt.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult?> DenyUnlessProductOwnerAsync(
        Guid productId, ClaimsPrincipal user, IMerchantAccessApi access,
        ICatalogModuleApi catalog, string capacite, CancellationToken ct)
    {
        if (IsAdmin(user))
        {
            return null;
        }

        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            // Authentifié sans rattachement commerçant : ce compte n'a aucune fiche.
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        var produit = await catalog.GetProductAsync(productId, ct);
        if (produit is null || produit.SellerId != acces.SellerId)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        // LA FICHE SITUE SA PROPRE BOUTIQUE — voir plus bas pourquoi c'est
        // `CanInStore` et non `Can` qui décide désormais.

        // 404 POUR « PAS À VOUS », 403 POUR « PAS VOUS » — ET L'ORDRE COMPTE.
        //
        // La propriété se vérifie D'ABORD. L'inverse ferait de la route un oracle
        // d'existence : un membre sans la capacité apprendrait, au 403 plutôt qu'au
        // 404, que la fiche visée appartient bien à son propre vendeur.
        //
        // Et une fois la propriété établie, le 404 devient nuisible : le membre VOIT
        // cette fiche dans son application, il vient d'y cliquer. « Introuvable »
        // enverrait le vendeur chercher un bug là où il n'y a qu'un rôle à élargir.
        //
        // ═════════════════════════════════════════════════════════════════════
        // `CanInStore` ET NON `Can` (lot F) — LA FICHE PORTE SA BOUTIQUE.
        //
        // `Can` répond sur l'union de tout ce que le membre porte, boutiques
        // confondues : un responsable de la boutique A pouvait donc dépublier les
        // fiches de la boutique B du même vendeur. Le vendeur multi-boutiques
        // n'avait aucun moyen de cloisonner ses équipes — c'est précisément ce que
        // la décision D27 compensait en INTERDISANT le second magasin à un membre
        // porteur d'un rôle de vocation boutique.
        //
        // `produit.StoreId` VAUT `null` SUR LES FICHES HISTORIQUES.
        //
        // `CanInStore(null, …)` retombe alors sur l'union — le comportement
        // d'avant. Refuser fermerait le catalogue ancien d'un vendeur à toute son
        // équipe, pour une donnée manquante dont personne n'est responsable.
        // ═════════════════════════════════════════════════════════════════════
        if (!acces.CanInStore(produit.StoreId, capacite))
        {
            return ApiResults.MissingCapability(capacite);
        }

        // AUCUNE CAPACITÉ DU CATALOGUE N'EST CRITIQUE AUJOURD'HUI, ET LA LIGNE
        // RESTE QUAND MÊME.
        //
        // Elle ne coûte qu'une recherche dans un ensemble. L'omettre ici rendrait la
        // garde du catalogue subtilement différente de celles des quatre autres
        // services — et le jour où une capacité produit serait promue Critique, le
        // step-up s'appliquerait partout SAUF ici, sans que rien ne le signale.
        if (MerchantCapabilities.RequiresStepUp(capacite) && !user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(capacite);
        }

        return null;
    }

    /// <summary>Refuse si l'offre n'appartient pas à l'appelant.</summary>
    private static async Task<IResult?> DenyUnlessOwnerAsync(
        Guid offerId, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, string capacite, CancellationToken ct)
    {
        if (IsAdmin(user))
        {
            return null;
        }

        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            // Authentifié sans rattachement commerçant : ce compte n'a aucune offre.
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        var offer = await offers.GetOfferAsync(offerId, ct);
        if (offer is null || offer.SellerId != acces.SellerId)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        // `OfferSummary.StoreId` N'EST PAS NULLABLE, contrairement à celui d'une
        // fiche : une offre EST une mise en vente dans une boutique, elle ne peut
        // pas ne pas en avoir. Le cadrage mord donc ici sans exception — y compris
        // sur le prix, qui est le geste le plus coûteux du service.
        if (!acces.CanInStore(offer.StoreId, capacite))
        {
            return ApiResults.MissingCapability(capacite);
        }

        // AUCUNE CAPACITÉ DU CATALOGUE N'EST CRITIQUE AUJOURD'HUI, ET LA LIGNE
        // RESTE QUAND MÊME.
        //
        // Elle ne coûte qu'une recherche dans un ensemble. L'omettre ici rendrait la
        // garde du catalogue subtilement différente de celles des quatre autres
        // services — et le jour où une capacité produit serait promue Critique, le
        // step-up s'appliquerait partout SAUF ici, sans que rien ne le signale.
        if (MerchantCapabilities.RequiresStepUp(capacite) && !user.HasRecentAuthentication())
        {
            return ApiResults.ReauthenticationRequired(capacite);
        }

        return null;
    }

    /// <summary>Détoure une photo et l'aplatit sur fond blanc.</summary>
    private static async Task<IResult> ProcessProductImageAsync(
        IFormFile file,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        IImageProcessor processor,
        IImageProcessingAvailability availability,
        CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════
        // RÉSERVÉE AUX VENDEURS, ET CE N'EST PAS UN EXCÈS DE ZÈLE.
        //
        // Cette route est la seule du service à consommer du CALCUL : chaque appel
        // déclenche une inférence u2net, quelques secondes de processeur pour une
        // image. Laissée au simple authentifié, elle offre à n'importe quel compte
        // acheteur un service d'inférence gratuit — et un moyen trivial de saturer
        // la machine en boucle, sans même de quoi le rattacher à un dossier.
        //
        // Le contrôle n'est pas d'appartenance : il n'y a pas encore de fiche à
        // posséder, la photo n'appartient à rien. C'est un contrôle de QUALITÉ
        // d'appelant, le même que pour la création de produit.
        //
        // MON PROPRE VÉRIFICATEUR L'A TROUVÉE. En croisant les routes du groupe
        // vendeur avec la présence d'une garde, celle-ci ressortait seule et non
        // gardée — parce que je venais de l'ajouter en pensant à la disponibilité
        // du service, pas à qui l'appelle.
        // ═════════════════════════════════════════════════════════════════════
        // `PRODUCT_UPDATE` : le détourage sert à préparer une photo de fiche.
        // Exiger `PRODUCT_CREATE` fermerait l'outil à qui retouche l'existant.
        if (await AccesVendeurAsync(user, access, ct) is not { } accesImage)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        if (!accesImage.Can(MerchantCapabilities.ProductUpdate))
        {
            return ApiResults.MissingCapability(MerchantCapabilities.ProductUpdate);
        }

        // 503 QUAND LE SERVICE N'EST PAS CONFIGURÉ, PAS 200 AVEC L'ORIGINAL.
        //
        // `NullImageProcessor` rend l'image intacte ET un succès : c'est le bon
        // comportement pour la création de produit — mieux vaut une photo brute
        // qu'un blocage — mais pas pour un geste dont le détourage EST l'objet.
        // Sans cette garde, l'application afficherait « Détourée » sur une image
        // strictement inchangée, et le vendeur chercherait la différence.
        if (!availability.IsAvailable)
        {
            // `Results.Problem` RENDAIT DU RFC 7807 AU MILIEU DE L'ENVELOPPE.
            //
            // `{ "title", "detail", "status" }` n'a aucun champ en commun avec
            // `{ "success", "error", "meta" }`. Un client qui lit `error.code` sur
            // toutes les autres réponses de ce service trouvait ici une forme
            // inconnue et retombait sur son message générique — sur le seul cas où
            // le message précis aurait évité un ticket de support.
            return ApiResults.Failure(
                ErrorCodes.DependencyUnavailable,
                "Le service de détourage n'est pas configuré sur cette instance.",
                StatusCodes.Status503ServiceUnavailable);
        }

        if (file.Length == 0)
        {
            return ApiResults.Failure(
                ErrorCodes.ValidationError,
                "Le fichier envoyé est vide.",
                StatusCodes.Status400BadRequest,
                [new ApiErrorDetail { Field = "file", Message = "Aucun octet reçu." }]);
        }

        // 12 Mo : au-delà, ce n'est plus une photo de produit prise au téléphone.
        // La borne protège l'inférence, qui charge l'image entière en mémoire.
        const long maxOctets = 12L * 1024 * 1024;
        if (file.Length > maxOctets)
        {
            // 413 conservé — c'est le status juste, et l'enveloppe ne change pas
            // le code HTTP, seulement la forme du corps.
            return ApiResults.Failure(
                ErrorCodes.ValidationError,
                "La photo ne doit pas dépasser 12 Mo.",
                StatusCodes.Status413PayloadTooLarge,
                [new ApiErrorDetail { Field = "file", Message = "Taille maximale : 12 Mo." }]);
        }

        using var flux = new MemoryStream();
        await file.CopyToAsync(flux, ct);

        var resultat = await processor.RemoveBackgroundWhiteAsync(
            file.FileName, file.ContentType, flux.ToArray(), ct);

        return resultat.Match(image => Results.File(image.Content, image.ContentType));
    }

    private static async Task<IResult> CreateOfferAsync(
        CreateOfferRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
    {
        // LE `SellerId` VIENT DU JETON, JAMAIS DU CORPS.
        //
        // C'est le défaut exact que `CreateProductAsync` porte encore quinze
        // lignes plus haut : accepter l'identifiant du vendeur dans la requête,
        // c'est laisser créer au nom d'autrui. La commande vérifie ensuite que la
        // fiche appartient bien à ce vendeur — deux barrières, pas une.
        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        // CADRÉ SUR `request.StoreId`, ET C'EST LE SEUL CAS OÙ LA BOUTIQUE VIENT
        // DU CORPS.
        //
        // Ailleurs elle est lue sur la ressource, donc inattaquable. Ici l'offre
        // n'existe pas encore : c'est l'appelant qui désigne où il veut vendre.
        // Ce n'est PAS une faille — désigner une boutique ne prouve rien, et le
        // cadrage se retourne contre lui : demander la boutique B quand on n'a de
        // droits que sur A produit un refus, là où `Can` aurait laissé passer.
        // La commande vérifie ensuite que la boutique appartient bien au vendeur.
        if (!acces.CanInStore(request.StoreId, MerchantCapabilities.OfferManage))
        {
            return ApiResults.MissingCapability(MerchantCapabilities.OfferManage);
        }

        // LE PRIX EST POSÉ À LA CRÉATION, DONC `OFFER_PRICE_UPDATE` AUSSI.
        //
        // Sans ce second contrôle, un membre privé du droit de repricer garderait
        // le chemin le plus court pour le contourner : archiver l'offre et la
        // recréer au prix voulu. La séparation des deux permissions ne tiendrait
        // pas une journée.
        if (!acces.CanInStore(request.StoreId, MerchantCapabilities.OfferPriceUpdate))
        {
            return ApiResults.MissingCapability(MerchantCapabilities.OfferPriceUpdate);
        }

        var sellerId = acces.SellerId;

        return (await sender.Send(new CreateOfferCommand(
            request.ProductId, request.VariantId, request.StoreId, sellerId,
            request.SellerPrice, request.Currency, request.Condition,
            request.FulfillmentType, request.ShipFromLocationId, request.HandlingTimeDays), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/catalog/seller/offers/{id}"));
    }

    /// <remarks>
    /// LA SEULE ROUTE DU GROUPE À GARDER `ISellerModuleApi` EN PLUS DE L'ACCÈS.
    ///
    /// `GetStoreAsync` répond « à quel vendeur appartient cette boutique » —
    /// question de structure, que `MerchantAccess` ne porte pas : il liste les
    /// boutiques du MEMBRE, pas celles du vendeur. Confondre les deux fermerait
    /// cette route à un propriétaire dont l'accès ne détaille aucune boutique.
    /// </remarks>
    private static async Task<IResult> ListStoreOffersAsync(
        Guid storeId, ClaimsPrincipal user, IMerchantAccessApi access, ISellerModuleApi sellers,
        ISender sender, CancellationToken ct)
    {
        // LA GARDE PORTE SUR LA BOUTIQUE, PAS SUR UNE OFFRE. On vérifie que la
        // boutique appartient au vendeur du jeton — sinon la liste des mises en
        // vente d'un concurrent serait lisible avec un seul identifiant.
        if (!IsAdmin(user))
        {
            if (await AccesVendeurAsync(user, access, ct) is not { } acces)
            {
                return ApiResults.NotFound(ServiceCodes.Catalog);
            }

            var boutique = await sellers.GetStoreAsync(storeId, ct);
            if (boutique is null || boutique.SellerId != acces.SellerId)
            {
                return ApiResults.NotFound(ServiceCodes.Catalog);
            }

            // `PRODUCT_VIEW` ET NON UNE CAPACITÉ D'OFFRE : c'est une LECTURE.
            // `OFFER_MANAGE` autorise à mettre en vente ; consulter ce qui est en
            // vente dans sa propre boutique relève du même droit que consulter les
            // fiches — sinon un gestionnaire de commandes ne verrait plus les prix
            // qu'il facture.
            // CADRÉ SUR LA BOUTIQUE DE L'URL — c'est la seule route du groupe où
            // la boutique est nommée par l'appelant plutôt que déduite d'une
            // ressource. Un responsable de la boutique A ne lit plus les prix de la
            // boutique B en changeant un identifiant.
            if (!acces.CanInStore(storeId, MerchantCapabilities.ProductView))
            {
                return ApiResults.MissingCapability(MerchantCapabilities.ProductView);
            }
        }

        return (await sender.Send(new ListStoreOffersQuery(storeId), ct)).Match(items => ApiResults.Ok(items));
    }

    private static async Task<IResult> ChangeOfferPriceAsync(
        Guid id, SellerPriceRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferPriceUpdate, ct)
        ?? (await sender.Send(new ChangeOfferPriceCommand(id, request.SellerPrice), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> SetOfferHandlingTimeAsync(
        Guid id, HandlingTimeRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferManage, ct)
        ?? (await sender.Send(new SetOfferHandlingTimeCommand(id, request.HandlingTimeDays), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ApplyOfferPromotionAsync(
        Guid id, PromotionRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferPriceUpdate, ct)
        ?? (await sender.Send(new ApplyOfferPromotionCommand(id, request.PromotionalSellerPrice, request.EndsOnUtc), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveOfferPromotionAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferPriceUpdate, ct)
        ?? (await sender.Send(new RemoveOfferPromotionCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ActivateOfferAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferManage, ct)
        ?? (await sender.Send(new ActivateOfferCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> PauseOfferAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferManage, ct)
        ?? (await sender.Send(new PauseOfferCommand(id), ct)).Match(() => Results.NoContent());

    /// <remarks>
    /// `DELETE` MAIS PAS UNE SUPPRESSION : `ArchiveOfferCommand` pose un état
    /// TERMINAL et garde la ligne. Une commande passée référence cette offre, et
    /// l'effacer laisserait un historique qui pointe vers rien.
    /// </remarks>
    private static async Task<IResult> ArchiveOfferAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access,
        IOfferModuleApi offers, ISender sender, CancellationToken ct)
        => await DenyUnlessOwnerAsync(id, user, access, offers, MerchantCapabilities.OfferManage, ct)
        ?? (await sender.Send(new ArchiveOfferCommand(id), ct)).Match(() => Results.NoContent());

    public sealed record CreateOfferRequest(
        Guid ProductId, Guid VariantId, Guid StoreId, decimal SellerPrice, string Currency,
        string Condition, string FulfillmentType, Guid ShipFromLocationId, int HandlingTimeDays);

    /// <param name="SellerPrice">Le prix NET vendeur. Le prix acheteur est calculé.</param>
    public sealed record SellerPriceRequest(decimal SellerPrice);

    public sealed record HandlingTimeRequest(int HandlingTimeDays);

    /// <param name="PromotionalSellerPrice">
    /// Prix NET VENDEUR promotionnel, strictement inférieur au net courant.
    ///
    /// CE N'EST PAS LE PRIX ACHETEUR. Le serveur y empile commission et frais,
    /// exactement comme pour le prix normal — c'est ce que l'application affiche
    /// en aperçu, et c'est le seul montant que le vendeur maîtrise.
    /// </param>
    public sealed record PromotionRequest(decimal PromotionalSellerPrice, DateTime? EndsOnUtc);

    private static async Task<IResult> ListBrandsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListBrandsQuery(), ct)).Match(items => ApiResults.Ok(items));

    private static async Task<IResult> GetBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetBrandQuery(id), ct)).Match(item => ApiResults.Ok(item));

    private static async Task<IResult> CreateBrandAsync(BrandRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateBrandCommand(request.Name, request.LogoUrl, request.Description), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/catalog/brands/{id}"));

    private static async Task<IResult> UpdateBrandAsync(Guid id, BrandRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateBrandCommand(id, request.Name, request.LogoUrl, request.Description), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> PublishBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishBrandCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UnpublishBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new UnpublishBrandCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteBrandAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteBrandCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ListCategoriesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCategoriesQuery(), ct)).Match(items => ApiResults.Ok(items));

    private static async Task<IResult> GetCategoryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCategoryQuery(id), ct)).Match(item => ApiResults.Ok(item));

    private static async Task<IResult> CreateCategoryAsync(CategoryRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCategoryCommand(
            request.Name, request.ParentId, request.ImageUrl, request.AttributeSchema), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/catalog/categories/{id}"));

    private static async Task<IResult> UpdateCategoryAsync(Guid id, CategoryRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCategoryCommand(id, request.Name, request.ImageUrl, request.AttributeSchema), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> PublishCategoryAsync(
        Guid id, CascadeRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishCategoryCommand(id, request.IncludeDescendants), ct))
            .Match(count => ApiResults.Ok(new { affected = count }));

    private static async Task<IResult> UnpublishCategoryAsync(
        Guid id, CascadeRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UnpublishCategoryCommand(id, request.IncludeDescendants), ct))
            .Match(count => ApiResults.Ok(new { affected = count }));

    private static async Task<IResult> DeleteCategoryAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteCategoryCommand(id), ct)).Match(() => Results.NoContent());

    // ═════════════════════════════════════════════════════════════════════════
    // VITRINE — ne rendent QUE la révision publiée des produits publiés (§17).
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> SearchPublicProductsAsync(
        string? query, Guid? categoryId, Guid? brandId, Guid? sellerId, string? condition,
        long? minPrice, long? maxPrice, string? sort, int? page, int? pageSize,
        ISender sender, CancellationToken ct)
        => (await sender.Send(new SearchPublicProductsQuery(
                query, categoryId, brandId, sellerId, condition,
                minPrice, maxPrice, sort, page ?? 1, pageSize ?? 20), ct))
            // `resultat` ET NON `page` : la méthode porte déjà un paramètre
            // nommé `page`, et le compilateur refuse qu'une lambda le redéclare.
            .Match(resultat => ApiResults.Page(resultat));

    private static async Task<IResult> GetPublicProductAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPublicProductQuery(id), ct)).Match(item => ApiResults.Ok(item));

    private static async Task<IResult> GetPublicProductBySlugAsync(string slug, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPublicProductBySlugQuery(slug), ct)).Match(item => ApiResults.Ok(item));

    /// <summary>
    /// La page boutique d'un vendeur, vue par un acheteur.
    ///
    /// ELLE PASSE PAR LA RECHERCHE, PAS PAR `ListProductsBySellerQuery`.
    ///
    /// Cette dernière rend tous les statuts et sert le back-office. Réutilisée ici,
    /// elle affichait au public les brouillons du vendeur — et la vitrine de la
    /// boutique était, en pratique, sa console d'administration.
    /// </summary>
    private static async Task<IResult> ListPublicSellerProductsAsync(
        Guid sellerId, int? page, int? pageSize, string? sort, ISender sender, CancellationToken ct)
        => (await sender.Send(new SearchPublicProductsQuery(
                SellerId: sellerId, Sort: sort, Page: page ?? 1, PageSize: pageSize ?? 20), ct))
            .Match(resultat => ApiResults.Page(resultat));

    // ═════════════════════════════════════════════════════════════════════════
    // GOUVERNANCE — tous les statuts, réservée aux administrateurs.
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListAllProductsAsync(
        int? page, int? pageSize, string? search, string? status, string? sort, string? dir, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllProductsQuery(page ?? 1, pageSize ?? 20, search, status, sort, dir), ct))
            .Match(resultat => ApiResults.Page(resultat));

    private static async Task<IResult> GetProductForAdminAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductQuery(id), ct)).Match(item => ApiResults.Ok(item));

    // ═════════════════════════════════════════════════════════════════════════
    // VALIDATION (§16)
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListPendingReviewsAsync(
        int? page, int? pageSize, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingReviewsQuery(page ?? 1, pageSize ?? 20), ct))
            .Match(resultat => ApiResults.Page(resultat));

    private static async Task<IResult> GetProductReviewsAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductReviewsQuery(id), ct)).Match(items => ApiResults.Ok(items));

    /// <summary>
    /// LE RELECTEUR VIENT DU JETON, JAMAIS DU CORPS.
    ///
    /// Le §22 le dit pour les commandes vendeur — « ne jamais accepter sellerId
    /// librement depuis le body » — et cela vaut plus encore ici : un relecteur pris
    /// dans la requête permettrait d'attribuer sa propre approbation à quelqu'un
    /// d'autre. Le journal `product_reviews` n'aurait alors plus aucune valeur
    /// d'audit, ce qui est sa seule raison d'exister.
    /// </summary>
    private static async Task<IResult> ApproveProductAsync(
        Guid id, ApproveRequest? request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } reviewerId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new ApproveProductCommand(id, reviewerId, request?.Comment), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> RejectProductAsync(
        Guid id, RejectRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } reviewerId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new RejectProductCommand(id, reviewerId, request.Comment, request.Reasons ?? []), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> SuspendProductAsync(
        Guid id, SuspendRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendProductCommand(id, request?.Reason), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> RestoreProductAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RestoreProductCommand(id), ct)).Match(() => Results.NoContent());

    // ═════════════════════════════════════════════════════════════════════════
    // RÉFÉRENTIEL D'ATTRIBUTS ET DEMANDES DE MARQUE (§10)
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListAttributeDefinitionsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAttributeDefinitionsQuery(), ct)).Match(items => ApiResults.Ok(items));

    private static async Task<IResult> CreateAttributeDefinitionAsync(
        AttributeDefinitionRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateAttributeDefinitionCommand(
                request.Code, request.Name, request.Type, request.Unit, request.Options), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/catalog/admin/attributes/{id}"));

    private static async Task<IResult> AssignAttributeToCategoryAsync(
        Guid id, CategoryAttributeRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AssignAttributeToCategoryCommand(
                id, request.AttributeDefinitionId, request.Required, request.Variant, request.DisplayOrder), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveAttributeFromCategoryAsync(
        Guid id, Guid attributeId, ISender sender, CancellationToken ct)
        => (await sender.Send(new RemoveAttributeFromCategoryCommand(id, attributeId), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> GetCategoryAttributesAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCategoryAttributesQuery(id), ct)).Match(items => ApiResults.Ok(items));

    private static async Task<IResult> ListPendingBrandRequestsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingBrandRequestsQuery(), ct)).Match(items => ApiResults.Ok(items));

    private static async Task<IResult> ApproveBrandRequestAsync(
        Guid id, ApproveBrandRequestBody? request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } reviewerId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new ApproveBrandRequestCommand(id, reviewerId, request?.ExistingBrandId), ct))
            .Match(brandId => ApiResults.Ok(new { brandId }));
    }

    private static async Task<IResult> RejectBrandRequestAsync(
        Guid id, RejectBrandRequestBody request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } reviewerId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new RejectBrandRequestCommand(id, reviewerId, request.Reason), ct))
            .Match(() => Results.NoContent());
    }

    /// <summary>
    /// LE VENDEUR VIENT DU JETON, PAS DU CORPS — MÊME RÈGLE QUE LA CRÉATION
    ///    DE PRODUIT (§22).
    ///
    /// Sans cela, un compte pourrait déposer des demandes au nom d'un autre vendeur,
    /// et la file d'administration deviendrait un vecteur de nuisance plutôt qu'un
    /// outil de gouvernance.
    /// </summary>
    private static async Task<IResult> RequestBrandCreationAsync(
        BrandRequestBody request, ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        // `PRODUCT_CREATE` : demander une marque n'a de sens que pour qui va
        // ensuite créer la fiche qui la porte.
        if (!acces.Can(MerchantCapabilities.ProductCreate))
        {
            return ApiResults.MissingCapability(MerchantCapabilities.ProductCreate);
        }

        var sellerId = acces.SellerId;

        return (await sender.Send(
                new RequestBrandCreationCommand(sellerId, request.Name, request.Note), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/catalog/admin/brands/requests/{id}"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // VENDEUR — ses propres fiches, brouillons compris.
    // ═════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListMyProductsAsync(
        ClaimsPrincipal user, IMerchantAccessApi access, ISender sender, CancellationToken ct)
    {
        // Même garde que la création : sans dossier vendeur, il n'y a pas de
        // catalogue à lire. 404 et non 403 — voir l'encadré de CreateProductAsync.
        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        if (!acces.Can(MerchantCapabilities.ProductView))
        {
            return ApiResults.MissingCapability(MerchantCapabilities.ProductView);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LA LISTE DISAIT CE QUE LE DÉTAIL REFUSAIT.
        //
        // Elle rendait TOUT le catalogue du vendeur — noms, statuts, brouillons non
        // publiés — à un responsable de la boutique A qui recevait ensuite un 404
        // dès qu'il en ouvrait une de la boutique B. Le cadrage du lot F tenait sur
        // la lecture unitaire et pas sur l'énumération : le vendeur multi-boutiques
        // croyait ses équipes cloisonnées, elles l'étaient à moitié.
        //
        // ON FILTRE, ON NE REFUSE PAS.
        //
        // Contrairement aux gardes de propriété, la question n'a pas de réponse
        // binaire ici : le membre a le droit de lire SA part du catalogue. Rendre
        // 403 fermerait un écran légitime ; rendre la liste complète le trahirait.
        //
        // LES FICHES SANS BOUTIQUE RESTENT VISIBLES DE TOUS.
        //
        // `CanInStore(null, …)` retombe sur l'union — même règle que la garde
        // unitaire, pour la même raison : le catalogue antérieur au rattachement
        // n'a de boutique à opposer à personne, et le cacher fermerait à toute
        // l'équipe un fonds que le vendeur a bel et bien constitué.
        // ═════════════════════════════════════════════════════════════════════
        var sellerId = acces.SellerId;

        var toutes = await sender.Send(new ListProductsBySellerQuery(sellerId), ct);
        if (toutes.IsFailure)
        {
            return toutes.Match(items => ApiResults.Ok(items));
        }

        return ApiResults.Ok(
            toutes.Value
                .Where(p => acces.CanInStore(p.StoreId, MerchantCapabilities.ProductView))
                .ToList());
    }

    private static async Task<IResult> GetMyProductAsync(
        Guid id, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductView, ct)
        ?? (await sender.Send(new GetProductQuery(id), ct)).Match(item => ApiResults.Ok(item));

    /// <summary>Crée une fiche produit AU NOM DU PORTEUR DU JETON.</summary>
    private static async Task<IResult> CreateProductAsync(
        CreateProductRequest request, ClaimsPrincipal user, IMerchantAccessApi access,
        ISender sender, CancellationToken ct)
    {
        // PAS DE `DenyUnlessProductOwnerAsync` ICI : il n'y a pas encore de
        // fiche à posséder. La garde équivalente, c'est cette résolution — sans
        // dossier vendeur, aucune création possible.
        //
        // 404 ET NON 403 pour un compte sans dossier : le motif diffère des
        // autres routes. Ce n'est pas pour taire l'existence d'une ressource, mais
        // parce qu'un acheteur qui découvre cette route n'a pas à apprendre qu'il
        // lui « manque seulement » un dossier vendeur pour écrire au catalogue.
        if (await AccesVendeurAsync(user, access, ct) is not { } acces)
        {
            return ApiResults.NotFound(ServiceCodes.Catalog);
        }

        // ═════════════════════════════════════════════════════════════════════
        // UN MEMBRE CADRÉ DOIT DIRE DANS QUELLE BOUTIQUE IL CRÉE.
        //
        // `CanInStore(null, …)` vaut `Can` — le repli qui préserve les vendeurs qui
        // ne désignent aucune boutique. Sur cette route-ci, ce repli était une porte
        // dérobée : un CATALOG_MANAGER affecté à la seule boutique A OMETTAIT le
        // champ, passait la garde par l'union, et la fiche naissait avec
        // `StoreId = null`. À partir de là `DenyUnlessProductOwnerAsync` retombait
        // elle aussi sur l'union POUR TOUTE LA VIE DE LA FICHE. Un champ omis, pas
        // un droit obtenu, suffisait à annuler le cadrage.
        //
        // LE REFUS NE VISE QUE LES MEMBRES RÉELLEMENT CADRÉS.
        //
        // Le propriétaire et les membres rattachés au VENDEUR n'ont aucune
        // affectation : pour eux `PermissionsByStore` est vide, la notion de
        // boutique n'a rien à contraindre, et `null` reste légitime. Exiger le champ
        // de tout le monde casserait la création chez les vendeurs mono-boutique qui
        // ne l'ont jamais renseigné.
        // ═════════════════════════════════════════════════════════════════════
        if (request.StoreId is null && acces.PermissionsByStore.Count > 0)
        {
            return ApiResults.Failure(
                ErrorCodes.ValidationError,
                "Précisez la boutique dans laquelle cette fiche est créée.",
                StatusCodes.Status400BadRequest,
                [new ApiErrorDetail { Field = "storeId", Message = "store_required_for_scoped_member" }]);
        }

        if (!acces.CanInStore(request.StoreId, MerchantCapabilities.ProductCreate))
        {
            return ApiResults.MissingCapability(MerchantCapabilities.ProductCreate);
        }

        var sellerId = acces.SellerId;

        // ARGUMENTS NOMMÉS, ET C'EST UNE LEÇON PAYÉE.
        //
        // Cet appel était positionnel. L'ajout de `Tarification` à la commande a
        // décalé six paramètres d'un cran : le compilateur a rendu onze erreurs
        // « conversion impossible », toutes à côté de la vraie cause. Et les deux
        // seuls types qui se seraient alignés — `Guid?` et `Guid?` — auraient
        // compilé en silence avec la marque à la place du groupe de produits.
        //
        // Nommer coûte une ligne de plus par argument et rend le prochain ajout
        // sans effet sur les appelants.
        return (await sender.Send(new CreateProductCommand(
            SellerId: sellerId,
            CategoryId: request.CategoryId,
            Name: request.Name,
            Description: request.Description,
            Tarification: request.Tarification,
            StoreId: request.StoreId,
            Condition: request.Condition,
            ShortDescription: request.ShortDescription,
            ProductType: request.ProductType,
            BrandId: request.BrandId,
            Gtin: request.Gtin,
            Ean: request.Ean,
            ProductGroupId: request.ProductGroupId,
            Attributes: request.Attributes,
            Tags: request.Tags,
            Specifications: request.Specifications), ct))
            .Match(id => ApiResults.Created(new { id }, $"/api/v1/catalog/seller/products/{id}"));
    }

    private static async Task<IResult> UpdateProductAsync(
        Guid id, ProductRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new UpdateProductCommand(
            ProductId: id,
            Name: request.Name,
            Description: request.Description,
            Tarification: request.Tarification,
            Condition: request.Condition,
            ShortDescription: request.ShortDescription,
            ProductType: request.ProductType,
            BrandId: request.BrandId,
            CategoryId: request.CategoryId,
            Gtin: request.Gtin,
            Ean: request.Ean,
            ProductGroupId: request.ProductGroupId,
            Attributes: request.Attributes,
            Tags: request.Tags,
            Specifications: request.Specifications), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, StatusRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, CapaciteDuStatut(request.Status), ct)
        ?? (await sender.Send(new ChangeProductStatusCommand(id, request.Status), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteProductAsync(Guid id, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new DeleteProductCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetTagsAsync(
        Guid id, TagsRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new SetProductTagsCommand(id, request.Tags), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> AddVariantAsync(
        Guid id, VariantRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new AddProductVariantCommand(
            id, request.Sku, request.Attributes, request.Barcode, request.WeightGrams,
            request.LengthMm, request.WidthMm, request.HeightMm), ct))
            .Match(variantId => ApiResults.Created(new { id = variantId }, $"/api/v1/catalog/seller/products/{id}/variants/{variantId}"));

    private static async Task<IResult> UpdateVariantAsync(
        Guid id, Guid variantId, VariantRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new UpdateProductVariantCommand(
            id, variantId, request.Sku, request.Attributes, request.Barcode, request.WeightGrams), ct))
            .Match(() => Results.NoContent());

    /// <summary>Retire une déclinaison de la vente, ou l'y remet.</summary>
    /// <remarks>
    /// REND LE NOMBRE D'OFFRES ARCHIVÉES, et l'application doit le montrer.
    ///
    /// Désactiver ferme les mises en vente de cette déclinaison, définitivement :
    /// `OfferStatus.Archived` est terminal. Un vendeur qui réactive ensuite ne
    /// retrouvera pas ses prix — il devra les refixer. Taire ce nombre ferait
    /// découvrir la conséquence après coup, sur un écran de mises en vente devenu
    /// vide.
    /// </remarks>
    private static async Task<IResult> SetVariantActiveAsync(
        Guid id, Guid variantId, StatusActiveRequest request,
        ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog,
        ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new SetVariantActiveCommand(id, variantId, request.Active), ct))
            .Match(archivees => ApiResults.Ok(new { archivedOffers = archivees }));

    private static async Task<IResult> RemoveVariantAsync(
        Guid id, Guid variantId, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new RemoveProductVariantCommand(id, variantId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> AddMediaAsync(
        Guid id, ProductMediaRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new AddProductMediaCommand(
            ProductId: id,
            MediaId: request.MediaId,
            Type: request.Type,
            AltText: request.AltText,
            IsPrimary: request.IsPrimary,

            // Le déposant du média doit être celui qui le rattache — voir
            // l'encadré d'`AddProductMediaCommandHandler`. Sans ce paramètre le
            // rattachement est REFUSÉ, pas autorisé : le défaut est fermé.
            RequestedByUserId: CurrentUserId(user) ?? Guid.Empty), ct))
            .Match(mediaId => ApiResults.Created(new { id = mediaId }, $"/api/v1/catalog/seller/products/{id}/media/{mediaId}"));

    private static async Task<IResult> RemoveMediaAsync(
        Guid id, Guid mediaId, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new RemoveProductMediaCommand(id, mediaId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetPrimaryMediaAsync(
        Guid id, Guid mediaId, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new SetPrimaryProductMediaCommand(id, mediaId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ReorderMediaAsync(
        Guid id, ReorderMediaRequest request, ClaimsPrincipal user, IMerchantAccessApi access, ICatalogModuleApi catalog, ISender sender, CancellationToken ct)
        => await DenyUnlessProductOwnerAsync(id, user, access, catalog, MerchantCapabilities.ProductUpdate, ct)
        ?? (await sender.Send(new ReorderProductMediaCommand(id, request.OrderedMediaIds), ct))
            .Match(() => Results.NoContent());

    public sealed record BrandRequest(string Name, string? LogoUrl, string? Description);

    public sealed record CategoryRequest(string Name, Guid? ParentId, string? ImageUrl, string? AttributeSchema);

    public sealed record CascadeRequest(bool IncludeDescendants = false);

    /// <summary>Création d'une fiche produit.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// IL N'Y A PLUS DE `SellerId` DANS CE CORPS, ET C'EST LE CŒUR DU #179.
    ///
    /// L'ancienne version l'acceptait. Un compte acheteur pouvait donc créer une
    /// fiche AU NOM d'un vendeur qu'il désignait lui-même : le produit apparaissait
    /// dans son catalogue, et les commandes lui étaient attribuées.
    ///
    /// LE RETIRER VAUT MIEUX QUE LE VÉRIFIER, et la nuance est le point le
    /// plus important de ce correctif.
    ///
    /// On aurait pu garder le champ et refuser quand il diffère du jeton. Le
    /// contrôle aurait été correct — jusqu'au prochain appelant qui l'oublierait,
    /// ou au prochain handler qui lirait le champ sans repasser par l'endpoint. Un
    /// champ d'identité présent dans un contrat public FINIT par être cru. Absent,
    /// il n'y a plus rien à oublier.
    ///
    /// UN CLIENT QUI ENVOIE ENCORE `sellerId` N'EST PAS REJETÉ : le
    /// désérialiseur ignore les propriétés inconnues. C'est voulu — on ne casse
    /// pas les applications déjà installées pour une clé devenue inutile. Elles
    /// cesseront simplement de pouvoir mentir.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <remarks>
    /// `Tarification` EST OBLIGATOIRE ET N'A PAS DE VALEUR PAR DÉFAUT.
    ///
    /// Une révision ne peut pas exister sans prix de référence (§8, §23). Un client
    /// qui ne l'envoie pas reçoit `catalog.pricing.required` — un refus explicite,
    /// pas une fiche à 0 F que la soumission rejetterait plus tard sans dire à
    /// quelle étape du formulaire on s'est arrêté.
    ///
    /// `Condition` peut être omis : voir `ProductCondition.Neuf()`.
    /// </remarks>
    public sealed record CreateProductRequest(
        Guid CategoryId,
        string Name,
        string Description,
        Guid? BrandId,
        string? Gtin,
        string? Ean,
        Guid? ProductGroupId,
        IReadOnlyDictionary<string, string>? Attributes,
        IReadOnlyList<string>? Tags,
        TarificationSaisie Tarification,
        ConditionSaisie? Condition = null,
        Guid? StoreId = null,
        string? ShortDescription = null,
        string? ProductType = null,
        IReadOnlyList<GroupeSpecSaisi>? Specifications = null);

    /// <summary>
    /// Modification d'une fiche existante.
    ///
    /// `SellerId` Y RESTE DÉCLARÉ MAIS N'EST PLUS LU. `UpdateProductCommand` ne
    /// l'a jamais pris : il ne servait qu'à la création. Le laisser dans le contrat
    /// de modification évite de casser les clients qui envoient le même objet pour
    /// les deux gestes — l'application vendeur, notamment.
    /// </summary>
    public sealed record ProductRequest(
        Guid SellerId,
        Guid CategoryId,
        string Name,
        string Description,
        Guid? BrandId,
        string? Gtin,
        string? Ean,
        Guid? ProductGroupId,
        IReadOnlyDictionary<string, string>? Attributes,
        IReadOnlyList<string>? Tags,
        TarificationSaisie Tarification,
        ConditionSaisie? Condition = null,
        string? ShortDescription = null,
        string? ProductType = null,
        IReadOnlyList<GroupeSpecSaisi>? Specifications = null);

    public sealed record StatusRequest(string Status);

    /// <summary>Approbation (§16). Le commentaire est facultatif.</summary>
    public sealed record ApproveRequest(string? Comment);

    /// <summary>
    /// Rejet motivé (§16).
    ///
    /// `Reasons` EST OBLIGATOIRE EN PRATIQUE, MÊME S'IL EST NULLABLE ICI.
    ///
    /// Le domaine refuse un rejet sans motif (`catalog.review.reason_required`) : le
    /// laisser nullable dans le contrat sert à rendre 422 avec un message utile
    /// plutôt que 400 sur un corps mal formé. Un vendeur qui apprend que sa fiche
    /// est refusée sans savoir quoi corriger resoumet à l'identique.
    /// </summary>
    public sealed record RejectRequest(string? Comment, IReadOnlyList<MotifSaisi>? Reasons);

    /// <summary>Suspension par la plateforme (§16). La raison est destinée au vendeur.</summary>
    public sealed record SuspendRequest(string? Reason);

    /// <summary>
    /// Création d'une définition d'attribut (§10).
    ///
    /// `Type` : TEXT, TEXTAREA, INTEGER, DECIMAL, BOOLEAN, SELECT, MULTI_SELECT,
    /// COLOR ou DATE. `Options` n'est attendu que pour les deux types à choix — et
    /// y est alors obligatoire, faute de quoi le formulaire vendeur afficherait une
    /// liste déroulante vide.
    /// </summary>
    public sealed record AttributeDefinitionRequest(
        string Code,
        string Name,
        string Type,
        string? Unit = null,
        IReadOnlyList<string>? Options = null);

    /// <summary>Rattachement d'un attribut à une catégorie (§10).</summary>
    public sealed record CategoryAttributeRequest(
        Guid AttributeDefinitionId,
        bool Required = false,
        bool Variant = false,
        int DisplayOrder = 0);

    /// <summary>Demande de marque déposée par un vendeur (§10).</summary>
    public sealed record BrandRequestBody(string Name, string? Note = null);

    /// <summary>
    /// Approbation d'une demande de marque (§16).
    ///
    /// `ExistingBrandId` EST LE CAS FRÉQUENT. Absent, une marque est créée ; posé,
    /// la demande est rattachée à une marque déjà au catalogue — ce que
    /// l'administrateur fait chaque fois qu'il reçoit une variante orthographique.
    /// </summary>
    public sealed record ApproveBrandRequestBody(Guid? ExistingBrandId);

    /// <summary>Refus motivé d'une demande de marque (§16).</summary>
    public sealed record RejectBrandRequestBody(string Reason);

    /// <param name="Active">
    /// `false` retire la déclinaison de la vente ET archive ses offres ; `true` la
    /// remet proposable, sans rétablir aucune offre.
    /// </param>
    public sealed record StatusActiveRequest(bool Active);

    public sealed record TagsRequest(IReadOnlyList<string> Tags);

    public sealed record VariantRequest(
        string Sku,
        IReadOnlyDictionary<string, string>? Attributes,
        string? Barcode,
        int WeightGrams,
        int? LengthMm,
        int? WidthMm,
        int? HeightMm);

    /// <summary>
    /// IL N'Y A PLUS D'`Url` ICI, ET C'EST LE FOND DE LA CORRECTION.
    ///
    /// Le client déposait le fichier au service média, puis renvoyait ICI
    /// l'identifiant ET l'adresse. Rien n'obligeait les deux à désigner la même
    /// chose : un vendeur pouvait donner un identifiant valide et l'adresse d'une
    /// image quelconque du web — une photo de concurrent, ou un pixel de suivi
    /// traçant chaque visiteur de la fiche.
    ///
    /// Laisser le champ en le SUPPRIMANT DU TRAITEMENT aurait été pire que de le
    /// retirer : il serait resté dans la documentation et dans les corps de requête,
    /// et le prochain lecteur aurait conclu qu'il sert à quelque chose. L'adresse
    /// vient désormais du service média, et de nulle part ailleurs.
    /// </summary>
    public sealed record ProductMediaRequest(Guid MediaId, string Type = "Image", string? AltText = null, bool IsPrimary = false);

    public sealed record ReorderMediaRequest(IReadOnlyList<Guid> OrderedMediaIds);
}
