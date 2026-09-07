using System.Security.Claims;
using HBA.Marketplace.ReturnRefund.Application.Commands;
using HBA.Marketplace.ReturnRefund.Application.Commands.CreateReturn;
using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Application.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Marketplace.ReturnRefund.Api.Endpoints;

/// <summary>LES RETOURS, CÔTÉ CLIENT.</summary>
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

    /// <summary>L'IDENTITÉ EST TRANSMISE À LA COMMANDE, ELLE N'EST PLUS DÉDUITE.</summary>
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
    /// Rend <c> null</c> quand le dossier appartient à l'appelant, ou la réponse à
    /// renvoyer sinon.
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

    /// <summary>ADMINISTRATEURS ET MODÉRATEURS PASSENT.</summary>
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
