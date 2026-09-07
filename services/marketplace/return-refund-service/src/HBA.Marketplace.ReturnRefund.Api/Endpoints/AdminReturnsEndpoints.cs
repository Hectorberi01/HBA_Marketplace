using System.Security.Claims;
using HBA.Marketplace.ReturnRefund.Application.Commands;
using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Application.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Marketplace.ReturnRefund.Api.Endpoints;

public static class AdminReturnsEndpoints
{
    public static IEndpointRouteBuilder MapAdminReturnsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/v1/admin/returns").WithTags("Admin Returns");

        // LA LISTE MANQUAIT, ET LES TROIS AUTRES ROUTES SONT ADRESSÉES PAR GUID.
        group.MapGet("/", ListAsync);

        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/override", OverrideAsync);
        group.MapPost("/{id:guid}/close", CloseAsync);
        return app;
    }

    /// <summary>Page de dossiers de retour, toutes boutiques confondues (Admin).</summary>
    private static async Task<IResult> ListAsync(
        int? page, int? pageSize, string? status, ISender sender, CancellationToken ct)
    {
        var demande = new ListAdminReturnsQuery(Page: page ?? 1, Status: status);

        var resultat = await sender.Send(
            pageSize is { } taille ? demande with { PageSize = taille } : demande, ct);

        return resultat.Match(donnees => ApiResults.Page(donnees));
    }

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetReturnQuery(id), ct)).Match(ApiResults.Ok);

    private static async Task<IResult> OverrideAsync(Guid id, ReasonDto reason, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => string.IsNullOrWhiteSpace(reason.Reason)
            ? ApiResults.Failure("return.override_reason_required", "Le motif d'override est obligatoire.", StatusCodes.Status400BadRequest)
            : (await sender.Send(new RejectReturnCommand(id, reason.Reason, CurrentUserId(user)), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> CloseAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => (await sender.Send(new CloseReturnCommand(id, CurrentUserId(user)), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
