using System.Security.Claims;
using HBA.Merchants.Contracts;
using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Promotions.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// API DU SERVICE PROMOTION (§10.16).
///
/// VALIDER N'EST PAS RÉSERVER, ET LES DEUX ROUTES NE SE RESSEMBLENT PAS PAR
/// HASARD.
///
/// `POST /promotions/validate` est en LECTURE PURE : l'écran du panier la rappelle
/// à chaque changement de quantité. Si elle réservait, dix modifications
/// consommeraient dix fois l'enveloppe et épuiseraient une campagne sans qu'aucune
/// commande ne soit passée. La réservation n'a pas de route REST du tout — elle
/// n'appartient qu'au checkout, donc au chemin gRPC, appelé par les services de
/// commande et non par une application cliente.
///
/// AUCUNE ROUTE ANONYME.
///
/// Le §10.16 exige un Bearer JWT sur les trois routes. Ce n'est pas une formalité
/// pour `validate` : le plafond par compte se compte sur un `UserId`, et une route
/// ouverte le rendrait indéterminable — donc inapplicable.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PromotionEndpoints
{
    public static IEndpointRouteBuilder MapPromotionEndpoints(this IEndpointRouteBuilder app)
    {
        var promotions = app.MapAuthenticatedGroup("/api/v1/promotions").WithTags("Promotions");

        promotions.MapPost("/validate", ValidateAsync).WithName("ValidateCoupon");

        // « merchant » AU SINGULIER, COMME LE CAHIER L'ÉCRIT.
        //
        // Le dépôt dit « Seller » partout ailleurs (décision D1), et l'écart est
        // assumé ici : ce chemin est un CONTRAT PUBLIC déjà publié au §10.16, que
        // des équipes ont lu. Aligner l'URL sur le vocabulaire interne aurait
        // rendu le document faux sans prévenir personne.
        var marchand = app.MapAuthenticatedGroup("/api/v1/merchant/promotions").WithTags("Promotions");

        // ═════════════════════════════════════════════════════════════════════
        // CES TROIS ROUTES ÉTAIENT FERMÉES À `RequireAdmin` PAR DÉFAUT DE
        // PROPRIÉTAIRE. ELLES NE LE SONT PLUS (D28).
        //
        // Ce qui était écrit ici, noir sur blanc : « la table `promotions` du
        // §10.16 n'a AUCUNE colonne de propriétaire. Il n'existe donc rien sur
        // quoi fonder un contrôle d'appartenance — un `RequireRole(Seller)` ne
        // dirait que "un vendeur", pas "CE vendeur", et laisserait chaque
        // marchand piloter les campagnes de tous les autres. »
        //
        // C'était exact, et c'était la bonne décision tant que la prise manquait.
        // `Promotion.OwnerSellerId` la fournit : la question « cette promotion
        // est-elle la vôtre ? » a enfin une réponse, et la fermeture n'a plus de
        // raison d'être.
        //
        // LE GROUPE RESTE `MapAuthenticatedGroup`, PAS `MapSellerGroup`.
        //
        // `MapSellerGroup` laisse entrer Admin ET MODERATOR. Or l'exclusion du
        // modérateur était une décision, pas un effet de bord : arbitrer des
        // contenus n'est pas décider de remises, et un modérateur n'a aucun
        // dossier vendeur — il passerait donc la porte du groupe pour se faire
        // refuser par la garde, ce qui est le pire des deux (une route qui a l'air
        // ouverte et ne l'est pas). La garde est donc ENTIÈRE dans les
        // gestionnaires : `DenyUnlessOwnPromotionAsync` n'accorde le passage qu'à
        // l'administrateur ou au vendeur propriétaire.
        //
        // LE COÛT DE CE CHOIX : LA SURFACE RESTE OUVERTE À TOUT COMPTE
        // AUTHENTIFIÉ, ET SEULE LA GARDE L'ARRÊTE.
        //
        // C'est exactement la fragilité que `MapSellerGroup` a été écrit pour
        // fermer — « cela tenait tant que chaque route portait sa garde ». Ici les
        // trois la portent, et une quatrième route ajoutée sans garde serait un
        // trou. Le jour où le modérateur aura un rôle à jouer sur les campagnes,
        // le bon geste sera de passer à `MapSellerGroup` et de retirer l'exclusion
        // de la garde — pas d'ajouter une exception de plus ici.
        // ═════════════════════════════════════════════════════════════════════
        marchand.MapPost("/", CreateAsync).WithName("CreatePromotion")
            .RequireIdempotency();
        marchand.MapGet("/", ListAsync).WithName("ListPromotions");
        marchand.MapDelete("/{id:guid}", CancelAsync).WithName("CancelPromotion");

        return app;
    }

    /// <summary>Corps de `POST /api/v1/promotions/validate` (§10.16).</summary>
    public sealed record ValidateRequest(
        string? Code, string? Scope, long Subtotal, long DeliveryFee, string? Currency);

    /// <summary>
    /// Valide un coupon pour un panier.
    ///
    /// UN COUPON REFUSÉ REND 200, PAS 422.
    ///
    /// Le §10.16 attend `{"valid": false}` : saisir un code périmé est un usage
    /// normal du champ, pas une requête malformée. Rendre une erreur ferait
    /// apparaître chaque frappe d'un client dans les alertes du service, et
    /// l'application devrait traiter un échec pour afficher un message qui n'a
    /// rien d'exceptionnel.
    /// </summary>
    private static async Task<IResult> ValidateAsync(
        ValidateRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (!TryUtilisateur(user, out var utilisateur))
        {
            return Unauthenticated();
        }

        var resultat = await sender.Send(
            new ValidateCouponQuery(
                request.Code,
                Univers(request.Scope),
                request.Subtotal,
                request.DeliveryFee,
                string.IsNullOrWhiteSpace(request.Currency) ? "XOF" : request.Currency!,
                utilisateur),
            ct);

        return resultat.Match(ApiResults.Ok);
    }

    /// <summary>
    /// Corps de `POST /api/v1/merchant/promotions` (§10.16).
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX FAÇONS DE DIRE LE FINANCEUR, ET UNE RÈGLE DE PRÉSÉANCE ÉCRITE.
    ///
    /// `FundedBy` est la forme lisible — « PLATFORM » ou « SELLER » — celle qu'un
    /// back-office affiche dans une liste déroulante et qu'un humain relit.
    /// `SellerFundedShareBps` est la forme exacte, en points de base, la seule qui
    /// sache dire « 60 / 40 ».
    ///
    /// Deux entrées pour une donnée sont une ambiguïté ; on la ferme ici plutôt
    /// que de laisser chaque appelant deviner : **`SellerFundedShareBps` l'emporte
    /// dès qu'il est fourni**. Le cas cofinancé est donc exprimable dès
    /// aujourd'hui sans changer ce contrat le jour où le commerce le demandera —
    /// ce que D28 laisse explicitement ouvert.
    ///
    /// Aucun des deux fourni = la PLATEFORME paie. Même défaut que la migration,
    /// même raison : un financeur non désigné ne peut pas être facturé à un
    /// marchand qui n'a rien signé.
    ///
    /// `OwnerSellerId` EST IGNORÉ QUAND L'APPELANT EST UN VENDEUR.
    ///
    /// Il est alors résolu depuis le JETON. L'accepter du corps de requête
    /// laisserait un marchand créer une campagne au nom d'un concurrent — et la
    /// lui faire financer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public sealed record CreateRequest(
        string? Name,
        string? Scope,
        string? Type,
        long Value,
        DateTime StartsAt,
        DateTime EndsAt,
        long? Budget,
        string? Currency,
        Dictionary<string, long>? Rules,
        string? FundedBy = null,
        int? SellerFundedShareBps = null,
        Guid? OwnerSellerId = null);

    /// <summary>
    /// Crée une campagne.
    ///
    /// LES RÈGLES ARRIVENT EN OBJET NOMMÉ, PAS EN LISTE DE COUPLES.
    ///
    /// Le §10.16 montre `"rules": { "minimumSubtotal": 5000 }`. La conversion vers
    /// le couple (type, json) attendu par le domaine se fait ici : c'est le bord
    /// HTTP qui doit parler la langue du document, pas le domaine.
    ///
    /// Un nom de règle inconnu est REFUSÉ par le domaine, jamais ignoré — voir
    /// l'encadré de `PromotionRule`. Une condition écartée en silence produirait
    /// une campagne moins restrictive que demandé.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateRequest request,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
    {
        var partVendeur = PartVendeur(request);
        var proprietaire = request.OwnerSellerId;

        if (!EstAdministrateur(user))
        {
            var acces = await AccesVendeurAsync(user, access, ct);

            if (acces.Refus is not null)
            {
                return acces.Refus;
            }

            // LE PROPRIÉTAIRE VIENT DU JETON, JAMAIS DU CORPS.
            //
            // C'est le défaut décrit dans `SellerReturnsEndpoints` : là-bas
            // `sellerId` était lié depuis la query string, et une requête suffisait
            // à lire le carnet d'un concurrent. Ici l'écriture serait pire — créer
            // une campagne au nom d'un autre marchand, et la lui faire financer.
            proprietaire = acces.Contexte!.SellerId;

            var refus = RefuserSiNonAutofinancee(acces.Contexte, partVendeur);

            if (refus is not null)
            {
                return refus;
            }
        }

        var resultat = await sender.Send(
            new CreatePromotionCommand(
                request.Name,
                Univers(request.Scope),
                Nature(request.Type),
                request.Value,
                request.StartsAt,
                request.EndsAt,
                request.Budget,
                string.IsNullOrWhiteSpace(request.Currency) ? "XOF" : request.Currency!,
                Regles(request.Rules),
                partVendeur,
                proprietaire),
            ct);

        return resultat.Match(vue => ApiResults.Created(vue, $"/api/v1/merchant/promotions/{vue.Id}"));
    }

    /// <summary>
    /// LE FILTRE D'APPARTENANCE EST POSÉ ICI, ET IL N'EST PAS OPTIONNEL.
    ///
    /// Un vendeur ne voit que SES campagnes : budgets, valeurs et taux sont des
    /// données commerciales, et les rendre toutes à quiconque porte le rôle
    /// `Seller` serait la fuite que `RequireAdmin` évitait. L'administrateur, lui,
    /// passe `null` et voit tout — c'est son écran de pilotage.
    ///
    /// Il n'existe PAS de paramètre de requête pour choisir le propriétaire. Le
    /// jour où un administrateur voudra filtrer sur un vendeur, ce sera un
    /// paramètre distinct, gardé, et non celui-ci rendu inscriptible.
    /// </summary>
    private static async Task<IResult> ListAsync(
        string? scope,
        int take,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
    {
        var univers = string.IsNullOrWhiteSpace(scope) ? (PromotionScope?)null : Univers(scope);

        Guid? proprietaire = null;

        if (!EstAdministrateur(user))
        {
            var acces = await AccesVendeurAsync(user, access, ct);

            if (acces.Refus is not null)
            {
                return acces.Refus;
            }

            if (!acces.Contexte!.Can(GererLesPromotions))
            {
                return ApiResults.MissingCapability(GererLesPromotions, RefusDeCapacite);
            }

            proprietaire = acces.Contexte.SellerId;
        }

        var resultat = await sender.Send(
            new ListPromotionsQuery(univers, take <= 0 ? 50 : take, proprietaire), ct);

        return resultat.Match(ApiResults.Ok);
    }

    /// <summary>
    /// Annule une campagne.
    ///
    /// ANNULER N'EFFACE PAS. `Cancel` bascule le statut ; les usages déjà
    /// engagés restent en base. Les supprimer priverait la comptabilité des
    /// remises réellement accordées, sur des commandes bel et bien payées.
    /// </summary>
    private static async Task<IResult> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
    {
        // LA LECTURE PRÉCÈDE LA GARDE, ET C'EST UN ALLER-RETOUR DE PLUS PAR
        // REQUÊTE.
        //
        // Le propriétaire d'une campagne n'est pas dans le jeton, il est dans la
        // RESSOURCE : sans cette lecture, il n'y a rien à comparer — et c'est
        // exactement l'état qui a valu `RequireAdmin` aux trois routes. Même forme
        // que `SellerReturnsEndpoints.ExecuterAsync` et que
        // `FinancialEndpoints.GetInvoiceAsync`, pour la même raison.
        var campagne = await sender.Send(new GetPromotionQuery(id), ct);

        if (campagne.IsFailure)
        {
            return campagne.Match(ApiResults.Ok);
        }

        var refus = await DenyUnlessOwnPromotionAsync(campagne.Value.OwnerSellerId, user, access, ct);

        if (refus is not null)
        {
            return refus;
        }

        return (await sender.Send(new CancelPromotionCommand(id), ct))
            .Match(() => Results.NoContent());
    }

    // ───────────────────────────────────────────────────────────────── Gardes

    /// <summary>
    /// La capacité qui garde les trois routes marchand.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `OFFER_PRICE_UPDATE` ET NON UN `PROMOTION_MANAGE` NEUF.
    ///
    /// Le catalogue de `MerchantCapabilities` n'a aucune permission promotionnelle.
    /// En créer une n'est PAS une ligne : `MerchantPermission` vit dans le DOMAINE
    /// de seller-service, `MerchantPermissions.Catalogue` porte son niveau de
    /// risque, `CapacitesTests` tient les deux listes synchrones, et les rôles
    /// existants devraient la recevoir par migration. C'est le lot d'un autre
    /// service.
    ///
    /// `OFFER_PRICE_UPDATE` est la plus proche du geste, et son propre commentaire
    /// le dit : « Changer le prix d'une offre, ou lui POSER UNE PROMOTION ». Elle
    /// est déjà séparée d'`OFFER_MANAGE` pour la bonne raison — « passer un article
    /// à 1 F CFA le liquide avant qu'aucune alerte ne parte » — qui est exactement
    /// le risque d'une campagne mal bornée.
    ///
    /// CE QUE CE CHOIX NE COUVRE PAS.
    ///
    /// Un membre autorisé à changer les prix peut désormais engager le budget
    /// promotionnel de son vendeur. Les deux gestes coûtent de l'argent au même
    /// marchand et se ressemblent, mais ils ne sont pas identiques — et si le
    /// commerce veut les séparer, il faudra la permission dédiée. C'est une dette
    /// nommée, pas un oubli.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private const string GererLesPromotions = MerchantCapabilities.OfferPriceUpdate;

    private const string RefusDeCapacite =
        "Cette campagne n'appartient pas à votre équipe, ou votre rôle ne porte pas cette capacité.";

    /// <summary>
    /// Rend <c>null</c> quand l'appelant a le droit d'agir sur cette campagne, ou
    /// le refus à renvoyer sinon.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX CONTRÔLES, PAS UN. C'est la règle du dépôt, posée par catalog :
    /// l'appartenance dit QUEL vendeur, la capacité dit SI l'on peut.
    ///
    /// UNE CAMPAGNE DE LA PLATEFORME (`OwnerSellerId == null`) RESTE UN GESTE
    /// D'ADMINISTRATEUR.
    ///
    /// C'est le cas de TOUTES les campagnes existantes — la migration les laisse à
    /// `null` parce qu'aucun vendeur n'a jamais pu en créer. Sans cette branche, un
    /// marchand dont le dossier vendeur est introuvable (`GetAccessAsync` rend
    /// `null`) et une campagne sans propriétaire se compareraient « null == null »,
    /// et n'importe quel compte sans équipe vendeur annulerait les campagnes de la
    /// plateforme. Le comparateur est écrit pour que ce cas soit IMPOSSIBLE, pas
    /// pour qu'il soit improbable.
    ///
    /// 403 ENVELOPPÉ, PAS 404.
    ///
    /// Règle §29 du dépôt et alignement issu de l'audit : quand la garde porte sur
    /// un VENDEUR, le refus est un 403 enveloppé avec un motif lisible. Le 404 est
    /// réservé aux identifiants de RESSOURCE qui ne sont pas publics — et ici la
    /// ressource a déjà été lue, donc son existence n'est plus le secret.
    ///
    /// MODERATOR N'EST PAS ADMIN ICI, CONTRAIREMENT À `DenyUnlessOwnSellerAsync`.
    ///
    /// L'exclusion vient de l'encadré d'origine de ce fichier : « arbitrer des
    /// contenus n'est pas décider de remises ». Elle est conservée telle quelle —
    /// la lever serait une décision, pas un effet de bord de ce lot.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult?> DenyUnlessOwnPromotionAsync(
        Guid? ownerSellerId, ClaimsPrincipal user, IMerchantAccessApi access, CancellationToken ct)
    {
        if (EstAdministrateur(user))
        {
            return null;
        }

        // Campagne de la plateforme : personne d'autre qu'un administrateur.
        if (ownerSellerId is not { } proprietaire)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Cette campagne appartient à la plateforme.",
                StatusCodes.Status403Forbidden);
        }

        var acces = await AccesVendeurAsync(user, access, ct);

        if (acces.Refus is not null)
        {
            return acces.Refus;
        }

        if (acces.Contexte!.SellerId != proprietaire)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Cette campagne n'est pas la vôtre.",
                StatusCodes.Status403Forbidden);
        }

        return acces.Contexte.Can(GererLesPromotions)
            ? null
            : ApiResults.MissingCapability(GererLesPromotions, RefusDeCapacite);
    }

    /// <summary>
    /// Un vendeur ne crée que des campagnes qu'il finance INTÉGRALEMENT.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// « QU'IL FINANCE » VEUT DIRE 100 %, PAS « AU MOINS UN PEU ».
    ///
    /// Une campagne cofinancée engage la trésorerie de la PLATEFORME. Laisser un
    /// marchand poser « part vendeur 10 % » reviendrait à lui laisser décider que
    /// la place de marché paie les 90 % restants — sur son propre budget, sans
    /// qu'aucun administrateur ne l'ait accepté. Le cofinancement est donc, comme
    /// la campagne plateforme, un geste d'administrateur.
    ///
    /// Le domaine, lui, sait déjà exprimer les trois cas : la restriction est
    /// D'AUTORISATION, pas de modèle. Le jour où un accord commercial encadrera le
    /// cofinancement à l'initiative du vendeur, c'est cette méthode qui changera,
    /// pas la colonne.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static IResult? RefuserSiNonAutofinancee(MerchantAccess acces, int partVendeur)
    {
        if (!acces.Can(GererLesPromotions))
        {
            return ApiResults.MissingCapability(GererLesPromotions, RefusDeCapacite);
        }

        if (partVendeur != PromotionFunding.SellerOnly)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Un vendeur ne peut créer qu'une campagne qu'il finance intégralement. "
                + "Une remise prise en charge, même en partie, par la plateforme relève d'un administrateur.",
                StatusCodes.Status403Forbidden);
        }

        return null;
    }

    /// <summary>
    /// Résout le contexte vendeur de l'appelant, ou le refus à lui renvoyer.
    ///
    /// `null` DE `GetAccessAsync` NE VEUT PAS DIRE « INTERDIT » MAIS « CE COMPTE
    /// N'A AUCUN DOSSIER VENDEUR » — le contrat le dit. Ici la conséquence est la
    /// même, et c'est à cette route de la traduire, pas au contrat.
    /// </summary>
    private static async Task<(MerchantAccess? Contexte, IResult? Refus)> AccesVendeurAsync(
        ClaimsPrincipal user, IMerchantAccessApi access, CancellationToken ct)
    {
        if (!TryUtilisateur(user, out var utilisateur))
        {
            return (null, Unauthenticated());
        }

        var acces = await access.GetAccessAsync(utilisateur, ct);

        return acces is null
            ? (null, ApiResults.MissingCapability(GererLesPromotions, RefusDeCapacite))
            : (acces, null);
    }

    /// <summary>
    /// ADMINISTRATEUR SEUL — LE MODÉRATEUR N'EST PAS ASSIMILÉ, VOIR
    /// <see cref="DenyUnlessOwnPromotionAsync"/>.
    /// </summary>
    private static bool EstAdministrateur(ClaimsPrincipal user)
        => user.IsInRole(ApiAuthorization.AdminRole);

    /// <summary>
    /// « SELLER » → 10 000 points de base. Voir l'encadré de <see cref="CreateRequest"/>
    /// pour la règle de préséance entre les deux formes.
    /// </summary>
    private static int PartVendeur(CreateRequest request)
    {
        if (request.SellerFundedShareBps is { } part)
        {
            // Hors bornes : on ne rectifie pas ici. `Promotion.Create` refuse avec
            // `promotions.funding_share_invalid`, et un plafonnement silencieux
            // transformerait une saisie de « 100 % » écrite « 100 » en une part de
            // 1 % — c'est-à-dire une campagne que le vendeur croirait financer.
            return part;
        }

        return request.FundedBy?.Trim().ToUpperInvariant() switch
        {
            "SELLER" => PromotionFunding.SellerOnly,
            _ => PromotionFunding.PlatformOnly
        };
    }

    // ─────────────────────────────────────────────────────────────── Traduction

    /// <summary>
    /// « FOOD » → <see cref="PromotionScope.Food"/>.
    ///
    /// Un univers inconnu devient `Global` : l'évaluation en devient PLUS stricte,
    /// puisque seules les campagnes globales passeront et qu'une campagne ciblée
    /// sera écartée par `EnsureApplicable`. C'est l'inverse d'une règle inconnue,
    /// qu'on refuse — ignorer une restriction accorde la remise qu'elle
    /// interdisait, alors qu'ignorer un ciblage n'en accorde aucune de plus.
    /// </summary>
    private static PromotionScope Univers(string? scope) => scope?.Trim().ToUpperInvariant() switch
    {
        "FOOD" => PromotionScope.Food,
        "MARKETPLACE" => PromotionScope.Marketplace,
        _ => PromotionScope.Global
    };

    /// <summary>
    /// « FREE_DELIVERY » → <see cref="PromotionType.FreeDelivery"/>.
    ///
    /// PAS DE VALEUR PAR DÉFAUT SILENCIEUSE ICI, CONTRAIREMENT AU SCOPE.
    ///
    /// Un type inconnu devient `Percent`, et `Promotion.Create` refusera toute
    /// valeur au-dessus de 100 — donc une saisie erronée ne peut pas produire une
    /// remise plus généreuse que demandé. Retomber sur `Fixed` aurait transformé
    /// « type: PERCENTAGE » (faute de frappe) en remise de 15 000 F au lieu de 15 %.
    /// </summary>
    private static PromotionType Nature(string? type) => type?.Trim().ToUpperInvariant() switch
    {
        "FIXED" => PromotionType.Fixed,
        "FREE_DELIVERY" => PromotionType.FreeDelivery,
        _ => PromotionType.Percent
    };

    /// <summary>« minimumSubtotal: 5000 » → (MINIMUM_SUBTOTAL, {"value":5000}).</summary>
    private static IReadOnlyList<PromotionRuleInput>? Regles(Dictionary<string, long>? rules)
    {
        if (rules is null || rules.Count == 0)
        {
            return null;
        }

        return rules
            .Select(paire => new PromotionRuleInput(
                NomDeRegle(paire.Key), $"{{\"value\": {paire.Value}}}"))
            .ToList();
    }

    /// <summary>
    /// « minimumSubtotal » → « MINIMUM_SUBTOTAL ».
    ///
    /// Un nom non reconnu est transmis TEL QUEL au domaine, qui le refusera. Le
    /// traduire en quelque chose de connu ferait appliquer une règle que
    /// l'appelant n'a pas demandée.
    /// </summary>
    private static string NomDeRegle(string cle)
        => PromotionConstantes.Convertir(cle);

    private static bool TryUtilisateur(ClaimsPrincipal user, out Guid userId)
    {
        var brut = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(brut, out userId);
    }

    private static IResult Unauthenticated()
        => Results.Json(
            ApiEnvelope.Fail("UNAUTHORIZED", "Authentification requise."),
            statusCode: StatusCodes.Status401Unauthorized);
}
