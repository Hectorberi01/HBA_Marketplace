using System.Security.Claims;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Drivers.Application.Accounts.Commands;
using HBA.Drivers.Application.Accounts.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Drivers.Api.Endpoints;

/// <summary>LA SURFACE DU LIVREUR SUR SON PROPRE DOSSIER.</summary>
public static class DriverEndpoints
{
    public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapAuthenticatedGroup("/api/v1/drivers/me").WithTags("Drivers · Mon dossier");

        me.MapPost("/", RegisterAsync).WithName("RegisterDriver").RequireIdempotency();
        me.MapGet("/", GetMineAsync).WithName("GetMyDriverAccount");
        me.MapPatch("/", UpdateProfileAsync).WithName("UpdateMyDriverProfile");
        me.MapGet("/vehicles", GetMyVehiclesAsync).WithName("ListMyDriverVehicles");
        me.MapPost("/vehicles", DeclareVehicleAsync).WithName("DeclareMyDriverVehicle");
        me.MapGet("/documents", GetMyDocumentsAsync).WithName("ListMyDriverDocuments");
        me.MapPost("/documents", SubmitDocumentAsync).WithName("SubmitMyDriverDocument");
        me.MapPost("/verification", SubmitDossierAsync).WithName("SubmitMyDriverDossier");

        // LA VÉRIFICATION EST UNE DÉCISION DE LA PLATEFORME, PAS DU LIVREUR.
        var admin = app.MapAdminGroup("/api/v1/admin/drivers").WithTags("Drivers · Exploitation");

        admin.MapGet("/", ListAsync).WithName("ListDriverAccounts");
        admin.MapGet("/{driverId:guid}", GetOneAsync).WithName("GetDriverAccount");
        admin.MapPost("/{driverId:guid}/verify", VerifyAsync).WithName("VerifyDriverAccount");
        admin.MapPost("/{driverId:guid}/reject", RejectAsync).WithName("RejectDriverAccount");
        admin.MapPost("/{driverId:guid}/suspend", SuspendAsync).WithName("SuspendDriverAccount");

        return app;
    }

    // ── Mon dossier ─────────────────────────────────────────────────────────

    private static async Task<IResult> RegisterAsync(
        RegisterDriverRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new RegisterDriverCommand(userId, request.FullName, request.Phone), ct))
            .Match(id => Results.Created("/api/v1/drivers/me", new { id }));
    }

    private static async Task<IResult> GetMineAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new GetMyDriverAccountQuery(userId), ct)).Match(Results.Ok);
    }

    private static async Task<IResult> UpdateProfileAsync(
        UpdateDriverProfileRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new UpdateDriverProfileCommand(userId, request.FullName, request.Phone), ct))
            .Match(() => Results.NoContent());
    }

    private static async Task<IResult> GetMyVehiclesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new GetMyDriverAccountQuery(userId), ct))
            .Match(account => Results.Ok(account.Vehicles));
    }

    private static async Task<IResult> DeclareVehicleAsync(
        DeclareVehicleRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new DeclareVehicleCommand(
                    userId, request.Type, request.Make, request.Model, request.Plate, request.CapacityKg),
                ct))
            .Match(id => Results.Created($"/api/v1/drivers/me/vehicles/{id}", new { id }));
    }

    private static async Task<IResult> GetMyDocumentsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        // RENDU AVEC LA LISTE DES PIÈCES MANQUANTES, pas seulement les pièces
        // déposées.
        return (await sender.Send(new GetMyDriverAccountQuery(userId), ct))
            .Match(account => Results.Ok(new { account.Documents, account.MissingDocuments }));
    }

    private static async Task<IResult> SubmitDocumentAsync(
        SubmitDriverDocumentRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(
                new SubmitDriverDocumentCommand(userId, request.Type, request.ObjectKey), ct))
            .Match(id => Results.Created($"/api/v1/drivers/me/documents/{id}", new { id }));
    }

    private static async Task<IResult> SubmitDossierAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return ApiResults.Unauthorized();
        }

        return (await sender.Send(new SubmitDriverDossierCommand(userId), ct))
            .Match(() => Results.Accepted());
    }

    // ── Exploitation ────────────────────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        ISender sender, CancellationToken ct, DriverVerificationStatus status = DriverVerificationStatus.UnderReview, int take = 100)
        => (await sender.Send(new ListDriverAccountsQuery(status, take), ct)).Match(Results.Ok);

    private static async Task<IResult> GetOneAsync(Guid driverId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDriverAccountQuery(driverId), ct)).Match(Results.Ok);

    private static async Task<IResult> VerifyAsync(Guid driverId, ISender sender, CancellationToken ct)
        => (await sender.Send(new VerifyDriverCommand(driverId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RejectAsync(
        Guid driverId, DecisionRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectDriverCommand(driverId, request.Reason), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> SuspendAsync(
        Guid driverId, DecisionRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendDriverCommand(driverId, request.Reason), ct))
            .Match(() => Results.NoContent());

    /// <summary>L'identité de l'appelant, et rien d'autre.</summary>
    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// AUCUN `DriverId` NI `UserId` DANS CE CORPS. C'est la garde, pas un oubli :
    /// une route d'inscription qui accepte l'identifiant de son propriétaire laisse
    /// n'importe quel compte ouvrir un dossier au nom d'un autre.
    /// </summary>
    public sealed record RegisterDriverRequest(string? FullName, string? Phone);

    public sealed record UpdateDriverProfileRequest(string? FullName, string? Phone);

    public sealed record DeclareVehicleRequest(
        DriverVehicleType Type, string? Make, string? Model, string? Plate, decimal? CapacityKg);

    /// <summary>`ObjectKey` désigne un objet déjà déposé chez media-service.</summary>
    public sealed record SubmitDriverDocumentRequest(DriverDocumentType Type, string? ObjectKey);

    public sealed record DecisionRequest(string? Reason);
}
