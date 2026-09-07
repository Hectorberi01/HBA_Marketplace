using System.Security.Claims;
using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Deliveries.Queries;
using HBA.Deliveries.Application.Drivers;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Deliveries.Api.Endpoints;

/// <summary>L'ESPACE DE TRAVAIL DU LIVREUR — LES ROUTES QUI MANQUAIENT.</summary>
public static class DriverDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDriverDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapAuthenticatedGroup("/api/deliveries/mine").WithTags("Delivery · Livreur");

        // ── La session de travail ────────────────────────────────────────────
        mine.MapPost("/online", GoOnlineAsync).WithName("DriverGoOnline");
        mine.MapPost("/offline", GoOfflineAsync).WithName("DriverGoOffline");
        mine.MapPost("/break", TakeBreakAsync).WithName("DriverTakeBreak");

        // LA ROUTE QUI REND LA LIVRAISON VIVANTE.
        mine.MapPost("/position", ReportPositionAsync).WithName("DriverReportPosition");

        // ── Le travail du jour ───────────────────────────────────────────────
        mine.MapGet("/", GetMineAsync).WithName("ListMyDeliveries");

        // ── La réponse à une proposition ─────────────────────────────────────
        mine.MapPost("/{id:guid}/accept", AcceptAsync).WithName("DriverAcceptDelivery");
        mine.MapPost("/{id:guid}/decline", DeclineAsync).WithName("DriverDeclineDelivery");

        // ── LES CINQ ÉTAPES D'EXÉCUTION ──────────────────────────────────────
        mine.MapPost("/{id:guid}/arrived-pickup", ArrivedAtPickupAsync).WithName("DriverArrivedAtPickup");
        mine.MapPost("/{id:guid}/picked-up", PickedUpAsync).WithName("DriverPickedUp");
        mine.MapPost("/{id:guid}/in-transit", InTransitAsync).WithName("DriverInTransit");
        mine.MapPost("/{id:guid}/arrived-dropoff", ArrivedAtDropoffAsync).WithName("DriverArrivedAtDropoff");

        // LA REMISE EST LA SEULE À PORTER UN CORPS : le code de preuve, dicté par
        // le DESTINATAIRE. Il n'est jamais rendu au livreur (voir `MyDeliveryDto`),
        // sans quoi la preuve ne prouverait rien.
        mine.MapPost("/{id:guid}/delivered", DeliveredAsync).WithName("DriverMarkDelivered");

        return app;
    }

    // ── Session ─────────────────────────────────────────────────────────────

    private static async Task<IResult> GoOnlineAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new GoOnlineCommand(driver.Value), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> GoOfflineAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new GoOfflineCommand(driver.Value), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> TakeBreakAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new TakeBreakCommand(driver.Value), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> ReportPositionAsync(
        PositionRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        if (driver.IsFailure)
        {
            return ApiResults.Match(driver, _ => Results.NoContent());
        }

        var command = new ReportDriverPositionCommand(driver.Value, request.Latitude, request.Longitude);
        return (await sender.Send(command, ct)).Match(() => Results.NoContent());
    }

    // ── Travail du jour ─────────────────────────────────────────────────────

    private static async Task<IResult> GetMineAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        if (driver.IsFailure)
        {
            return ApiResults.Match(driver, _ => Results.NoContent());
        }

        return (await sender.Send(new MyDeliveriesQuery(driver.Value), ct)).Match(Results.Ok);
    }

    // ── Proposition ─────────────────────────────────────────────────────────

    private static async Task<IResult> AcceptAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new AcceptDeliveryCommand(id, driver.Value), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> DeclineAsync(
        Guid id, DeclineRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new DeclineDeliveryCommand(id, driver.Value, request.Reason), ct))
                .Match(() => Results.NoContent());
    }

    // ── Progression ─────────────────────────────────────────────────────────

    private static async Task<IResult> ArrivedAtPickupAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new MarkArrivedAtPickupCommand(id, driver.Value), ct))
                .Match(() => Results.NoContent());
    }

    private static async Task<IResult> PickedUpAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new MarkPickedUpCommand(id, driver.Value), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> InTransitAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new MarkInTransitCommand(id, driver.Value), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> ArrivedAtDropoffAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new MarkArrivedAtDropoffCommand(id, driver.Value), ct))
                .Match(() => Results.NoContent());
    }

    private static async Task<IResult> DeliveredAsync(
        Guid id, ProofRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var driver = await ResolveAsync(user, sender, ct);
        return driver.IsFailure
            ? ApiResults.Match(driver, _ => Results.NoContent())
            : (await sender.Send(new MarkDeliveredCommand(id, request.ProofValue, driver.Value), ct))
                .Match(() => Results.NoContent());
    }

    /// <summary>LE COMPTE DU JETON, TRADUIT EN LIVREUR.</summary>
    private static async Task<Result<Guid>> ResolveAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        return Guid.TryParse(raw, out var userId)
            ? await sender.Send(new ResolveDriverQuery(userId), ct)
            : Result.Failure<Guid>(
                Error.Unauthorized("driver.unauthenticated", "Aucun compte dans le jeton présenté."));
    }

    /// <summary>Position transmise par le téléphone du livreur.</summary>
    public sealed record PositionRequest(double Latitude, double Longitude);

    public sealed record DeclineRequest(string? Reason);

    /// <summary>Le code dicté par le destinataire.</summary>
    public sealed record ProofRequest(string? ProofValue);
}
