using System.Security.Claims;
using HBA.Analytics.Application.RollUps;
using HBA.Analytics.Application.RollUps.Queries;
using HBA.Merchants.Contracts;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Analytics.Api.Endpoints;

/// <summary>Surface HTTP du service Analytics : trois lectures, rien d'autre.</summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        // `MapSellerGroup` ferme la porte à l'ACHETEUR au niveau du groupe ; la
        // garde d'appartenance ci-dessous ferme celle des AUTRES vendeurs.
        var seller = app.MapSellerGroup("/api/sellers/{sellerId:guid}/analytics")
            .WithTags("Seller · Analytics");

        seller.MapGet("/sales", GetSellerSalesAsync);

        // MÊME GARDE, MÊME CAPACITÉ.
        seller.MapGet("/cancellations", GetSellerCancellationsAsync);

        // LE BACK-OFFICE, SUR RÔLE ET NON SUR PERMISSION.
        var admin = app.MapAdminGroup("/api/admin/analytics").WithTags("Admin · Analytics");

        admin.MapGet("/activity", GetPlatformActivityAsync);
        admin.MapGet("/signups", GetSignupsAsync);

        // LE TAUX D'ÉCHEC PAR PRESTATAIRE — le graphe que le document de conception
        // nommait « CETTE ABSENCE VAUT D'ÊTRE REGARDÉE POUR ELLE-MÊME ».
        admin.MapGet("/payments", GetPaymentsAsync);

        return app;
    }

    /// <summary>Les ventes du vendeur, jour par jour.</summary>
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
        => await DenyUnlessOwnSellerAsync(sellerId, user, access, ct)
        ?? (await sender.Send(new GetSellerSalesSeriesQuery(sellerId, from, to, currency), ct))
            .Match(Results.Ok);

    /// <summary>Les annulations du vendeur, jour par jour.</summary>
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
        // boutique.
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
