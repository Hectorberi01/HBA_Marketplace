using System.Security.Claims;
using HBA.Marketplace.ReturnRefund.Application.Commands;
using HBA.Marketplace.ReturnRefund.Application.Commands.CreateReturn;
using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Application.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Marketplace.ReturnRefund.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES RETOURS, CÔTÉ CLIENT.
///
/// TROIS ROUTES NE LISAIENT AUCUNE IDENTITÉ.
///
/// Le groupe est authentifié — `MapAuthenticatedGroup` — mais `CreateAsync`,
/// `GetAsync` et `TimelineAsync` ne regardaient pas QUI appelait. Le client d'un
/// dossier était simplement lu dans la commande désignée. Conséquences, avec un
/// seul identifiant glané dans un ticket de support ou une capture d'écran :
///
///   • ouvrir un retour sur la commande d'un tiers, en son nom ;
///   • lire son dossier — lignes achetées, montants, adresse de la boutique ;
///   • lire sa chronologie complète.
///
/// LE REFUS SE PRÉSENTE COMME UNE ABSENCE, PAS COMME UN INTERDIT.
///
/// Ici l'identifiant désigne une RESSOURCE, pas un vendeur : la règle §29 du
/// dépôt demande un 404. Et c'est aussi le bon choix de sécurité — répondre 403
/// confirmerait à un inconnu que ce dossier existe, ce qui est déjà la moitié de
/// ce qu'il cherchait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CustomerReturnsEndpoints
{
    public static IEndpointRouteBuilder MapCustomerReturnsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAuthenticatedGroup("/api/v1/marketplace/returns").WithTags("Marketplace Returns");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListMineAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/cancel", CancelAsync);
        group.MapPost("/{id:guid}/evidence", AddEvidenceAsync);
        group.MapGet("/{id:guid}/timeline", TimelineAsync);

        return app;
    }

    /// <summary>
    /// L'IDENTITÉ EST TRANSMISE À LA COMMANDE, ELLE N'EST PLUS DÉDUITE.
    ///
    /// La garde ne peut pas vivre ici : à ce stade on ne connaît que
    /// l'identifiant de commande, et savoir qui l'a passée exige l'appel gRPC que
    /// le handler fait déjà. On lui passe donc l'appelant, et c'est lui qui
    /// compare — un seul aller-retour, une seule source de vérité.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateReturnRequestDto request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateReturnCommand(request, CurrentUserId(user)), ct))
            .Match(dto => ApiResults.Created(dto, $"/api/v1/marketplace/returns/{dto.ReturnId}"));

    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, int page, int pageSize, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } customerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetCustomerReturnsQuery(customerId, page, pageSize), ct)).Match(ApiResults.Page);

    private static async Task<IResult> GetAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var dossier = await sender.Send(new GetReturnQuery(id), ct);
        if (dossier.IsFailure)
        {
            return dossier.Match(ApiResults.Ok);
        }

        return EstLeSien(dossier.Value, user)
            ? ApiResults.Ok(dossier.Value)
            : Introuvable();
    }

    private static async Task<IResult> CancelAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var garde = await VerifierProprietaireAsync(id, user, sender, ct);
        if (garde is not null)
        {
            return garde;
        }

        return (await sender.Send(new CancelReturnCommand(id, CurrentUserId(user)), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> AddEvidenceAsync(
        Guid id, AddEvidenceDto request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var garde = await VerifierProprietaireAsync(id, user, sender, ct);
        if (garde is not null)
        {
            return garde;
        }

        return (await sender.Send(
                new AddEvidenceCommand(id, request.MediaId, request.Kind, request.Caption, CurrentUserId(user)), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> TimelineAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var garde = await VerifierProprietaireAsync(id, user, sender, ct);
        if (garde is not null)
        {
            return garde;
        }

        return (await sender.Send(new GetReturnTimelineQuery(id), ct)).Match(ApiResults.Ok);
    }

    /// <summary>
    /// Rend <c>null</c> quand le dossier appartient à l'appelant, ou la réponse à
    /// renvoyer sinon.
    ///
    /// UNE LECTURE DE PLUS PAR REQUÊTE, ET ELLE EST NÉCESSAIRE. Le client d'un
    /// dossier n'est pas dans le jeton, il est dans la ressource : sans la lire,
    /// il n'y a rien à comparer. C'est exactement l'état d'avant.
    /// </summary>
    private static async Task<IResult?> VerifierProprietaireAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is null)
        {
            return Results.Unauthorized();
        }

        var dossier = await sender.Send(new GetReturnQuery(id), ct);
        if (dossier.IsFailure)
        {
            return dossier.Match(ApiResults.Ok);
        }

        return EstLeSien(dossier.Value, user) ? null : Introuvable();
    }

    /// <summary>
    /// ADMINISTRATEURS ET MODÉRATEURS PASSENT.
    ///
    /// Ils arbitrent les litiges et n'ont aucun dossier à leur nom : les exclure
    /// ici casserait le support. Leur surface propre est `/api/v1/admin/returns`,
    /// mais tant qu'un agent ouvre le lien que le client lui a envoyé, il tombe
    /// sur cette route.
    /// </summary>
    private static bool EstLeSien(ReturnRequestDto dossier, ClaimsPrincipal user)
        => CurrentUserId(user) is { } userId
           && (dossier.CustomerId == userId
               || user.IsInRole(ApiAuthorization.AdminRole)
               || user.IsInRole(ApiAuthorization.ModeratorRole));

    private static IResult Introuvable()
        => ApiResults.NotFound("return.not_found", "Retour introuvable.");

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
