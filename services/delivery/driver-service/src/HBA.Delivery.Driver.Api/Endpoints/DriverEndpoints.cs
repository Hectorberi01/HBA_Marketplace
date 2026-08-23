using System.Security.Claims;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Drivers.Application.Accounts.Commands;
using HBA.Drivers.Application.Accounts.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Drivers.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA SURFACE DU LIVREUR SUR SON PROPRE DOSSIER.
///
/// CE QUI ÉTAIT CASSÉ, ET CE N'ÉTAIT PAS UN DÉTAIL (ISSUE-029, CRITICAL).
///
/// Les six routes `/api/v1/drivers/me*` prenaient toutes `store.DefaultDriverId`,
/// un GUID codé en dur dans `DriverStore`. Autrement dit : TOUS LES LIVREURS
/// ÉTAIENT LE MÊME LIVREUR. Le livreur A lisait le dossier de B, modifiait son
/// numéro de téléphone, déclarait un véhicule à sa place et voyait ses courses.
/// Aucune route n'ouvrait le `ClaimsPrincipal` — le service savait qui appelait
/// et ne s'en servait pas.
///
/// LA CORRECTION N'EST PAS « VÉRIFIER L'IDENTIFIANT REÇU ».
///
/// C'est le raisonnement écrit dans `FinancialEndpoints.cs` autour des routes
/// `/me`, et il vaut ici mot pour mot : le propriétaire du dossier EST
/// l'utilisateur du jeton, il n'y a donc aucun lien à vérifier et surtout aucune
/// surface où il faudrait le vérifier. Aucune de ces routes ne prend
/// d'identifiant de livreur, ni en paramètre, ni dans le corps. Un identifiant
/// accepté puis « vérifié » dépend d'une garde qu'il suffit d'oublier une fois —
/// c'est la faille ISSUE-017/018, refermée à la vague 1 et rouverte deux fois
/// depuis.
///
/// CE GROUPE N'EXIGE PAS LE RÔLE `Driver`, ET C'EST DÉLIBÉRÉ.
///
/// `ApiAuthorization` explique pourquoi : le rôle est semé mais n'est attribué
/// qu'À LA VÉRIFICATION du dossier. L'exiger ici fermerait la porte à
/// l'inscription elle-même — un candidat livreur ne peut pas être livreur avant
/// d'avoir déposé ses pièces. La garde est donc l'appartenance : sans dossier,
/// ces routes rendent 404.
///
/// CE QUI A DISPARU DE CE FICHIER, ET OÙ C'EST PARTI.
///
///   • `POST /me/availability` et `GET /me/deliveries` : la disponibilité et le
///     carnet de courses vivent dans `deliveries.drivers`, chez delivery-service,
///     et c'est LUI que le dispatch lit. Les tenir ici aurait donné deux
///     écrivains sur un même fait, dont l'un — celui qui décide de proposer une
///     course — aurait toujours lu l'autre avec retard. Elles sont désormais sous
///     `/api/deliveries/mine` (voir `DriverDeliveryEndpoints`).
///
///   • Le groupe `/internal/v1/drivers` : il n'était protégé que par la politique
///     de repli, c'est-à-dire par « un jeton, n'importe lequel ». Tout compte
///     authentifié pouvait donc lire le dossier d'un livreur par son identifiant,
///     et surtout appeler `POST /{driverId}/busy-state` pour rendre un livreur
///     occupé — donc l'exclure du dispatch. Le transport interne de ce dépôt est
///     gRPC, dont l'interception à clé partagée est une VRAIE garde (voir
///     `InternalRoutes`). Ces routes ont été retirées, pas déplacées.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // LA VÉRIFICATION EST UNE DÉCISION DE LA PLATEFORME, PAS DU LIVREUR.
        //
        // AVANT CE LOT, ELLE N'EXISTAIT NULLE PART : `DriverStore` naissait
        // avec un livreur déjà « VERIFIED » et aucune route ne pouvait changer
        // cet état. « Vérifié » ne voulait donc rien dire, et la seule garde du
        // dispatch — `AccountStatus is Active` — était toujours satisfaite.
        //
        // Ces routes portent un `driverId` dans l'URL, et ce n'est PAS une
        // entorse à la règle du jeton : l'appelant n'est pas le titulaire du
        // dossier, il l'arbitre. Il n'y a rien à déduire de son jeton. La
        // protection est le rôle, exactement comme les files d'administration
        // de `FinancialEndpoints`.
        // ═════════════════════════════════════════════════════════════════════
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
        // déposées. Sans elle, l'écran livreur ne peut afficher que « dossier
        // incomplet » et le livreur redépose au hasard.
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

    /// <summary>
    /// L'identité de l'appelant, et rien d'autre. Copie assumée de
    /// `FinancialEndpoints.CurrentUserId` et de ses six jumelles : ces six lignes
    /// n'ont jamais été factorisées dans ce dépôt, et les factoriser maintenant
    /// serait un changement transverse sans rapport avec le défaut corrigé ici.
    /// </summary>
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

    /// <summary>
    /// `ObjectKey` désigne un objet déjà déposé chez media-service.
    ///
    /// NI SON EXISTENCE NI SON PROPRIÉTAIRE NE SONT VÉRIFIÉS — voir l'encadré
    /// de `DriverDocument`. Un livreur peut donc présenter la clé du permis d'un
    /// autre. La vérification humaine est le seul contrôle en place.
    /// </summary>
    public sealed record SubmitDriverDocumentRequest(DriverDocumentType Type, string? ObjectKey);

    public sealed record DecisionRequest(string? Reason);
}
