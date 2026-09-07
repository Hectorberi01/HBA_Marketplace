using System.Security.Claims;
using HBA.Merchants.Contracts;
using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Promotions.Api.Endpoints;

/// <summary>API DU SERVICE PROMOTION (§10.16).</summary>
public static class PromotionEndpoints
{
    public static IEndpointRouteBuilder MapPromotionEndpoints(this IEndpointRouteBuilder app)
    {
        var promotions = app.MapAuthenticatedGroup("/api/v1/promotions").WithTags("Promotions");

        promotions.MapPost("/validate", ValidateAsync).WithName("ValidateCoupon");

        // « merchant » AU SINGULIER, COMME LE CAHIER L'ÉCRIT.
        var marchand = app.MapAuthenticatedGroup("/api/v1/merchant/promotions").WithTags("Promotions");

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
        marchand.MapPost("/", CreateAsync).WithName("CreatePromotion")
            .RequireIdempotency();
        marchand.MapGet("/", ListAsync).WithName("ListPromotions");
        marchand.MapDelete("/{id:guid}", CancelAsync).WithName("CancelPromotion");

        return app;
    }

    /// <summary>Corps de `POST /api/v1/promotions/validate` (§10.16).</summary>
    public sealed record ValidateRequest(
        string? Code, string? Scope, long Subtotal, long DeliveryFee, string? Currency);

    /// <summary>Valide un coupon pour un panier.</summary>
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

    /// <summary>Corps de `POST /api/v1/merchant/promotions` (§10.16).</summary>
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

    /// <summary>Crée une campagne.</summary>
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

    /// <summary>LE FILTRE D'APPARTENANCE EST POSÉ ICI, ET IL N'EST PAS OPTIONNEL.</summary>
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

    /// <summary>Annule une campagne.</summary>
    private static async Task<IResult> CancelAsync(
        Guid id,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
    {
        // LA LECTURE PRÉCÈDE LA GARDE, ET C'EST UN ALLER-RETOUR DE PLUS PAR
        // REQUÊTE.
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

    /// <summary>La capacité qui garde les trois routes marchand.</summary>
    private const string GererLesPromotions = MerchantCapabilities.OfferPriceUpdate;

    private const string RefusDeCapacite =
        "Cette campagne n'appartient pas à votre équipe, ou votre rôle ne porte pas cette capacité.";

    /// <summary>
    /// Rend <c>null</c> quand l'appelant a le droit d'agir sur cette campagne, ou
    /// le refus à renvoyer sinon.
    /// </summary>
    /// <remarks>
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

    /// <summary>Un vendeur ne crée que des campagnes qu'il finance INTÉGRALEMENT.</summary>
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

    /// <summary>Résout le contexte vendeur de l'appelant, ou le refus à lui renvoyer.</summary>
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

    /// <summary>« SELLER » → 10 000 points de base.</summary>
    private static int PartVendeur(CreateRequest request)
    {
        if (request.SellerFundedShareBps is { } part)
        {
            // Hors bornes : on ne rectifie pas ici.
            return part;
        }

        return request.FundedBy?.Trim().ToUpperInvariant() switch
        {
            "SELLER" => PromotionFunding.SellerOnly,
            _ => PromotionFunding.PlatformOnly
        };
    }

    // ─────────────────────────────────────────────────────────────── Traduction

    /// <summary>« FOOD » → <see cref="PromotionScope.Food"/>.</summary>
    private static PromotionScope Univers(string? scope) => scope?.Trim().ToUpperInvariant() switch
    {
        "FOOD" => PromotionScope.Food,
        "MARKETPLACE" => PromotionScope.Marketplace,
        _ => PromotionScope.Global
    };

    /// <summary>« FREE_DELIVERY » → <see cref="PromotionType.FreeDelivery"/>.</summary>
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

    /// <summary>« minimumSubtotal » → « MINIMUM_SUBTOTAL ».</summary>
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
