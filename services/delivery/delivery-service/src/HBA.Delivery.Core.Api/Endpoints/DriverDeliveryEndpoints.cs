using System.Security.Claims;
using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Deliveries.Queries;
using HBA.Deliveries.Application.Drivers;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Deliveries.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ESPACE DE TRAVAIL DU LIVREUR — LES ROUTES QUI MANQUAIENT.
///
/// CE GROUPE ÉTAIT DÉCRIT PARTOUT DANS LE CODE ET N'EXISTAIT NULLE PART.
///
/// `IDeliveryRepository.ListActiveForDriverAsync`, `MyDeliveriesQuery`,
/// `DeliveryProgressCommands` et `Delivery.AcceptByDriver` portent tous un encadré
/// qui parle de « `/api/deliveries/mine` » au présent. Ce chemin n'était mappé par
/// aucun `Program.cs`. Six commandes correctes, testées, avec leurs gardes — et
/// pas un seul appelant. Le livreur n'avait aucun moyen de voir une proposition,
/// de l'accepter, ni de faire avancer une course.
///
/// L'IDENTITÉ VIENT DU JETON, POUR CHACUNE DES ONZE ROUTES.
///
/// Aucune ne prend de `driverId` — ni en paramètre, ni dans le corps. Le jeton
/// porte un `userId` ; `ResolveDriverQuery` le traduit. C'est la même règle que
/// `FinancialEndpoints` applique à `/wallets/me` (ISSUE-017/018), et elle compte
/// davantage ici : `MarkDelivered` déclenche le GAIN du livreur. Un identifiant
/// accepté en paramètre aurait laissé n'importe quel compte authentifié clôturer
/// la course d'un autre et en encaisser la part.
///
/// LA GARDE D'AFFECTATION EST `RequiredDriverId`, ET ELLE EST TOUJOURS
/// RENSEIGNÉE ICI.
///
/// Les commandes de progression l'acceptent nul — c'est le chemin de
/// l'exploitation, qui débloque une course à la main. Depuis ces routes-ci il est
/// TOUJOURS écrit, et il vient de `ResolveDriverQuery`. Le handler rend alors
/// « introuvable » — et non « interdit » — si la course est confiée à quelqu'un
/// d'autre : un 403 confirmerait au demandeur qu'elle existe.
///
/// CE GROUPE N'EXIGE PAS LE RÔLE `Driver`, ET CE N'EST PAS UN OUBLI.
///
/// `ApiAuthorization` le dit : le rôle est semé mais n'est attribué qu'à la
/// vérification du dossier, et poser `RequireRole(Driver)` verrouillerait tous les
/// livreurs déjà en activité. La garde réelle est l'appartenance :
/// `ResolveDriverQuery` rend 404 à qui n'a pas de ligne dans `deliveries.drivers`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DriverDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDriverDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapAuthenticatedGroup("/api/deliveries/mine").WithTags("Delivery · Livreur");

        // ── La session de travail ────────────────────────────────────────────
        mine.MapPost("/online", GoOnlineAsync).WithName("DriverGoOnline");
        mine.MapPost("/offline", GoOfflineAsync).WithName("DriverGoOffline");
        mine.MapPost("/break", TakeBreakAsync).WithName("DriverTakeBreak");

        // ═════════════════════════════════════════════════════════════════════
        // LA ROUTE QUI REND LA LIVRAISON VIVANTE.
        //
        // C'est le seul appelant de `IDriverLocationCache.SetAsync` de toute la
        // plateforme. Sans elle, `DispatchDeliveryCommandHandler` interroge un
        // cache vide, ne trouve jamais personne, et aucune course n'est jamais
        // proposée (D30).
        //
        // PAS DE `RequireIdempotency` : c'est un battement, pas une création.
        // Rejouer une position écrase la précédente par la même valeur, ce qui est
        // exactement le comportement voulu.
        // ═════════════════════════════════════════════════════════════════════
        mine.MapPost("/position", ReportPositionAsync).WithName("DriverReportPosition");

        // ── Le travail du jour ───────────────────────────────────────────────
        mine.MapGet("/", GetMineAsync).WithName("ListMyDeliveries");

        // ── La réponse à une proposition ─────────────────────────────────────
        mine.MapPost("/{id:guid}/accept", AcceptAsync).WithName("DriverAcceptDelivery");
        mine.MapPost("/{id:guid}/decline", DeclineAsync).WithName("DriverDeclineDelivery");

        // ═════════════════════════════════════════════════════════════════════
        // ── LES CINQ ÉTAPES D'EXÉCUTION ──────────────────────────────────────
        //
        // Elles suivent l'ordre de la machine à états ; c'est l'agrégat qui refuse
        // une transition prise à l'envers, jamais la route. Une route qui
        // vérifierait elle-même l'état serait une seconde source de vérité, et
        // `DeliveryStateMachine` cesserait d'être la référence.
        // ═════════════════════════════════════════════════════════════════════
        mine.MapPost("/{id:guid}/arrived-pickup", ArrivedAtPickupAsync).WithName("DriverArrivedAtPickup");
        mine.MapPost("/{id:guid}/picked-up", PickedUpAsync).WithName("DriverPickedUp");
        mine.MapPost("/{id:guid}/in-transit", InTransitAsync).WithName("DriverInTransit");
        mine.MapPost("/{id:guid}/arrived-dropoff", ArrivedAtDropoffAsync).WithName("DriverArrivedAtDropoff");

        // LA REMISE EST LA SEULE À PORTER UN CORPS : le code de preuve, dicté
        // par le DESTINATAIRE. Il n'est jamais rendu au livreur (voir
        // `MyDeliveryDto`), sans quoi la preuve ne prouverait rien.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COMPTE DU JETON, TRADUIT EN LIVREUR.
    ///
    /// CE N'EST PAS UNE COMMODITÉ, C'EST LA GARDE DE TOUT CE FICHIER. Le jeton
    /// porte un `userId` ; les courses portent un `driverId`. Sans cette
    /// traduction, la seule façon d'écrire ces routes serait d'accepter le
    /// `driverId` de l'appelant — et une garde qui vérifie un identifiant reçu est
    /// une garde qu'il suffit d'oublier une fois.
    ///
    /// L'échec est rendu tel quel : `ResolveDriverQuery` produit un 401 si le jeton
    /// ne porte aucun compte, et un 404 si le compte n'est pas un livreur.
    /// `onSuccess` n'est jamais appelé sur ce chemin — il n'est là que pour
    /// satisfaire la signature de `Match`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<Result<Guid>> ResolveAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

        return Guid.TryParse(raw, out var userId)
            ? await sender.Send(new ResolveDriverQuery(userId), ct)
            : Result.Failure<Guid>(
                Error.Unauthorized("driver.unauthenticated", "Aucun compte dans le jeton présenté."));
    }

    /// <summary>
    /// Position transmise par le téléphone du livreur.
    ///
    /// AUCUN `DriverId` DANS CE CORPS. C'est exactement le champ que le lot
    /// 5.1/5.3 a dû retirer de `LocationBatchRequest` chez tracking-service : une
    /// route de position qui accepte l'identifiant du livreur permet à n'importe
    /// quel compte de faire croire qu'un livreur est ailleurs, donc de détourner
    /// les propositions de course.
    /// </summary>
    public sealed record PositionRequest(double Latitude, double Longitude);

    public sealed record DeclineRequest(string? Reason);

    /// <summary>
    /// Le code dicté par le destinataire. Nul quand la course n'exige qu'une photo
    /// — c'est `ProofPolicy` qui a tranché à la création, pas l'appelant (D35).
    /// </summary>
    public sealed record ProofRequest(string? ProofValue);
}
