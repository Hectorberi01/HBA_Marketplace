using System.Security.Claims;
using HBA.Analytics.Application.RollUps;
using HBA.Analytics.Application.RollUps.Queries;
using HBA.Merchants.Contracts;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Analytics.Api.Endpoints;

/// <summary>Surface HTTP du service Analytics : trois lectures, rien d'autre.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUNE ROUTE D'ÉCRITURE, ET IL NE DOIT JAMAIS Y EN AVOIR.
///
/// Les chiffres de ce service viennent des événements, et d'eux seuls. Une route
/// qui corrigerait une ligne de roll-up à la main créerait une valeur que rien ne
/// peut reproduire : au premier recalcul, elle disparaîtrait sans que personne ne
/// sache qu'elle avait été posée. Un rattrapage d'historique — le point 4 du
/// document de conception — se fait par un import ponctuel et journalisé, pas par
/// une route.
///
/// LE VENDEUR EST DÉSIGNÉ PAR L'URL, PAS PAR LE JETON.
///
/// C'est le choix d'order-service sur `/api/sellers/{sellerId}/orders`, et c'est
/// délibérément le même : un MEMBRE d'équipe agit pour le compte de son vendeur
/// et n'a pas de dossier vendeur à son nom. Résoudre le vendeur depuis le jeton
/// fermerait la console à toute l'équipe sauf au propriétaire — c'est
/// exactement le défaut que `IMerchantAccessApi` a été écrit pour fermer.
///
/// 403 ET NON 404 SUR UN VENDEUR QUI N'EST PAS LE VÔTRE : l'existence d'un
/// vendeur n'est pas un secret — les boutiques sont publiques. La règle du dépôt
/// est explicite : identifiant de VENDEUR venu de l'URL → 403 ; identifiant de
/// RESSOURCE → 404.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // `MapSellerGroup` ferme la porte à l'ACHETEUR au niveau du groupe ; la
        // garde d'appartenance ci-dessous ferme celle des AUTRES vendeurs. Les
        // deux sont nécessaires : le groupe seul laisserait un vendeur lire les
        // chiffres d'un concurrent, la garde seule ferait dépendre la protection
        // du fait que personne n'oublie de la poser sur la prochaine route.
        var seller = app.MapSellerGroup("/api/sellers/{sellerId:guid}/analytics")
            .WithTags("Seller · Analytics");

        seller.MapGet("/sales", GetSellerSalesAsync);

        // MÊME GARDE, MÊME CAPACITÉ. `SELLER_ANALYTICS_VIEW` ouvre « les chiffres
        // de ce vendeur » : ses ventes et ce qu'il a perdu sont la même
        // information vue des deux côtés, et les séparer en deux capacités
        // donnerait un droit qu'aucun rôle ne saurait attribuer sans l'autre.
        seller.MapGet("/cancellations", GetSellerCancellationsAsync);

        // ═════════════════════════════════════════════════════════════════════
        // LE BACK-OFFICE, SUR RÔLE ET NON SUR PERMISSION.
        //
        // Le document de conception annonçait un `PLATFORM_ANALYTICS_VIEW`. Il
        // n'a pas été créé, et c'est un choix qu'il faut pouvoir défendre : le
        // catalogue `MerchantPermission` décrit ce qu'un membre d'une ÉQUIPE
        // VENDEUR peut faire chez SON vendeur. Y déclarer un droit sur les
        // chiffres de la plateforme entière aurait mis dans le catalogue vendeur
        // une permission qu'aucun vendeur ne doit jamais porter, et que le
        // contrôle `permissions` aurait exigé de garder quelque part.
        //
        // L'administration de cette plateforme est gouvernée par des RÔLES —
        // `MapAdminGroup` = Admin ou Modérateur —, comme les vingt et une autres
        // surfaces `/api/admin/*` du dépôt. C'est ce qui est employé ici.
        // ═════════════════════════════════════════════════════════════════════
        var admin = app.MapAdminGroup("/api/admin/analytics").WithTags("Admin · Analytics");

        admin.MapGet("/activity", GetPlatformActivityAsync);
        admin.MapGet("/signups", GetSignupsAsync);

        // LE TAUX D'ÉCHEC PAR PRESTATAIRE — le graphe que le document de
        // conception nommait « CETTE ABSENCE VAUT D'ÊTRE REGARDÉE POUR
        // ELLE-MÊME ». Il existe parce que `PaymentCaptured` et `PaymentFailed`
        // portent enfin `Provider`, `Amount` et `Currency`.
        admin.MapGet("/payments", GetPaymentsAsync);

        return app;
    }

    /// <summary>
    /// Les ventes du vendeur, jour par jour. Permission `SELLER_ANALYTICS_VIEW`.
    /// </summary>
    /// <remarks>
    /// LES TROIS PARAMÈTRES SONT DES SCALAIRES, PAS UN TYPE `…Request`.
    ///
    /// Un `GET` dont le gestionnaire prend un type suffixé `Request` sans
    /// `[FromBody]` fait échouer le DÉMARRAGE du service : ASP.NET Core tente de
    /// le lier depuis le corps, qu'un GET n'a pas. Le contrôle `config-et-gardes`
    /// refuse ce cas précisément parce qu'il ne se voit qu'à l'exécution.
    /// </remarks>
    private static async Task<IResult> GetSellerSalesAsync(
        Guid sellerId,
        DateOnly? from,
        DateOnly? to,
        string? currency,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
        // `??` ET NON UN TERNAIRE : la garde rend `null` quand elle laisse passer.
        // C'est la forme employee par les six autres services qui portent cette
        // garde — s'en ecarter ferait relire l'enchainement a chaque fois.
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, ct)
        ?? (await sender.Send(new GetSellerSalesSeriesQuery(sellerId, from, to, currency), ct))
            .Match(Results.Ok);

    /// <summary>
    /// Les annulations du vendeur, jour par jour. Permission `SELLER_ANALYTICS_VIEW`.
    /// </summary>
    private static async Task<IResult> GetSellerCancellationsAsync(
        Guid sellerId,
        DateOnly? from,
        DateOnly? to,
        string? currency,
        ClaimsPrincipal user,
        IMerchantAccessApi access,
        ISender sender,
        CancellationToken ct)
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, ct)
        ?? (await sender.Send(new GetSellerCancellationSeriesQuery(sellerId, from, to, currency), ct))
            .Match(Results.Ok);

    /// <summary>Les paiements de la plateforme, jour par jour et par prestataire.</summary>
    private static async Task<IResult> GetPaymentsAsync(
        DateOnly? from, DateOnly? to, string? currency, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentSeriesQuery(from, to, currency), ct)).Match(Results.Ok);

    /// <summary>L'activité de la plateforme, jour par jour.</summary>
    private static async Task<IResult> GetPlatformActivityAsync(
        DateOnly? from, DateOnly? to, string? currency, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPlatformActivitySeriesQuery(from, to, currency), ct))
            .Match(Results.Ok);

    /// <summary>Les inscriptions, jour par jour.</summary>
    private static async Task<IResult> GetSignupsAsync(
        DateOnly? from, DateOnly? to, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSignupSeriesQuery(from, to), ct)).Match(Results.Ok);

    /// <summary>
    /// L'appelant est-il ce vendeur, et a-t-il le droit d'en lire les chiffres ?
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// COPIE DE FORME D'`OrderEndpoints.DenyUnlessOwnSellerAsync`, ET C'EST
    /// ASSUMÉ.
    ///
    /// La même garde existe dans order-service, financial-service, catalog,
    /// inventory et review. Six copies d'une même vérification, c'est six
    /// occasions de diverger — et c'est le prix de l'autonomie par service, payé
    /// ici en connaissance de cause. Ce qui est PARTAGÉ, et qui est la partie qui
    /// compte, c'est la réponse : `IMerchantAccessApi` est le seul à savoir qui
    /// est qui.
    ///
    /// L'ADMINISTRATION PASSE. Elle voit tous les carnets de commandes ; lui
    /// refuser les chiffres qui vont avec obligerait à ouvrir un second chemin
    /// pour la même information.
    ///
    /// `null` D'`GetAccessAsync` NE VEUT PAS DIRE « INTERDIT » : il veut dire
    /// « ce compte n'appartient à aucune équipe vendeur ». Ici, la conséquence
    /// est la même qu'un vendeur qui n'est pas le sien — 403.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static async Task<IResult?> DenyUnlessOwnSellerAsync(
        Guid sellerId, ClaimsPrincipal user, IMerchantAccessApi access, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (user.IsInRole(ApiAuthorization.AdminRole) || user.IsInRole(ApiAuthorization.ModeratorRole))
        {
            return null;
        }

        var acces = await access.GetAccessAsync(userId, ct);

        if (acces is null || acces.SellerId != sellerId)
        {
            return ApiResults.Failure(
                ErrorCodes.Forbidden,
                "Ces chiffres ne sont pas les vôtres.",
                StatusCodes.Status403Forbidden);
        }

        // `Can` ET NON `CanInStore` : un roll-up journalier n'appartient à aucune
        // boutique. Les parts vendeur de `OrderConfirmed` ne portent pas de
        // boutique — `OrderLine` n'en connaît pas —, donc la question « dans
        // quelle boutique ? » n'a pas de réponse à poser. C'est une limite du
        // SCHÉMA d'order-service, nommée comme telle dans `IMerchantAccessApi`,
        // et c'est là qu'il faudra la lever.
        return acces.Can(MerchantCapabilities.SellerAnalyticsView)
            ? null
            : ApiResults.MissingCapability(MerchantCapabilities.SellerAnalyticsView);
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
