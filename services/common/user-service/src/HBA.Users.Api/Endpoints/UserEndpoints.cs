using System.Security.Claims;
using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using HBA.Users.Application.Addresses;
using HBA.Users.Application.Devices;
using HBA.Users.Application.Preferences;
using HBA.Users.Application.Profiles;
using MediatR;

namespace HBA.Users.Api.Endpoints;

/// <summary>Endpoints HTTP du service User : profil affichable, avatar et adresses.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // PRÉFIXE VERSIONNÉ DU §10.2 : `/api/v1/users`, ET NON `/api/users`.
        //
        // Ce n'est pas un simple renommage : la passerelle route `/api/users/{**catch-all}`
        // en dur dans `apps/api-gateway/.../appsettings.json`, et `RoutingTests` teste ce
        // chemin. Les trois doivent bouger ensemble — sinon le service répond sur un
        // chemin que plus personne n'appelle, et la passerelle rend 404 sur un service
        // parfaitement sain.
        var users = app.MapAuthenticatedGroup("/api/v1/users").WithTags("Users");

        users.MapGet("/me", GetProfileAsync).WithName("GetMyUserProfile");

        // §10.2 : « PATCH /api/v1/users/me — met à jour les champs éditables ». Le
        // service n'exposait que `/me/profile` (nom) et `/me/avatar` (avatar), deux
        // appels là où le contrat en promet un. Les deux anciens restent : ils sont
        // utilisés par le portail vendeur, et les retirer dans le même lot mêlerait
        // une mise en conformité à une rupture de client.
        users.MapPatch("/me", UpdateProfileAsync).WithName("UpdateMyUserProfile").AllowIdempotency();
        users.MapGet("/me/profile", GetProfileAsync).WithName("GetMyUserProfileDetails");
        users.MapPatch("/me/profile", RenameProfileAsync).WithName("RenameMyUserProfile");

        users.MapGet("/me/avatar", GetProfileAsync).WithName("GetMyUserAvatar");
        users.MapPut("/me/avatar", SetAvatarAsync).WithName("SetMyUserAvatar");

        // §10.2 : préférences et appareils. Les deux tables du contrat n'avaient
        // aucun agrégat ; elles en ont un désormais, et ces quatre routes les servent.
        users.MapGet("/me/preferences", GetPreferencesAsync).WithName("GetMyPreferences");
        users.MapPut("/me/preferences", UpdatePreferencesAsync).WithName("UpdateMyPreferences").AllowIdempotency();

        users.MapGet("/me/devices", ListDevicesAsync).WithName("ListMyDevices");
        users.MapPost("/me/devices", RegisterDeviceAsync).WithName("RegisterMyDevice").RequireIdempotency();

        users.MapGet("/me/addresses", ListAddressesAsync).WithName("ListMyAddresses");
        // §5 : `Idempotency-Key` obligatoire sur les POST de création. Sans elle, une
        // reprise réseau du client crée une seconde adresse identique dans le carnet.
        users.MapPost("/me/addresses", AddAddressAsync).WithName("AddMyAddress").RequireIdempotency();
        users.MapPut("/me/addresses/{id:guid}", UpdateAddressAsync).WithName("UpdateMyAddress");
        users.MapDelete("/me/addresses/{id:guid}", DeleteAddressAsync).WithName("DeleteMyAddress");
        users.MapPut("/me/addresses/{id:guid}/default", SetDefaultAddressAsync).WithName("SetMyDefaultAddress");

        // ═════════════════════════════════════════════════════════════════════
        // LE RÉFÉRENTIEL GÉOGRAPHIQUE — IL EXISTAIT, PERSONNE NE LE PUBLIAIT.
        //
        // `BeninGeography` porte les 12 départements et les 77 communes depuis
        // l'origine, et six services s'en servent pour valider une commune et
        // résoudre un libellé. Mais c'est une classe de DOMAINE, compilée dans
        // chaque service : aucun endpoint ne la rendait, et la passerelle n'avait
        // rien sous `/api/geo`.
        //
        // Conséquence visible dans les deux applications : le sélecteur de
        // commune appelait `/seller/geo/communes` — le BFF du monolithe —
        // recevait 404, et affichait « La liste des communes n'est pas encore
        // disponible ». Un message de panne pour une route qui n'a jamais existé
        // côté HBA, et un champ obligatoire impossible à remplir.
        //
        // POURQUOI PAS UNE TABLE EN BASE — TROIS RAISONS, DONT UNE INVISIBLE.
        //
        //   1. Le découpage n'a pas bougé depuis la réforme de 1999. Une table
        //      demanderait une migration, un écran d'administration et une
        //      reprise, pour des lignes que personne n'éditera.
        //   2. Le DOMAINE valide contre cette liste (`IsKnownCommune`). Une table
        //      créerait une SECONDE autorité : le jour où elles divergent, le
        //      formulaire propose une commune que le domaine refuse — ou
        //      l'inverse — et l'écart ne se voit qu'à la saisie.
        //   3. Les migrations SQL replient les accents avec la MÊME table de
        //      caractères que `BeninGeography.Normalize` (BeninAddressModel,
        //      BeninOrderShippingAddress, BeninLocationAddress,
        //      BeninSellerCommune). Tout le rattrapage des adresses saisies en
        //      texte libre repose sur cette équivalence. Une troisième copie des
        //      communes en base la mettrait en danger en silence.
        //
        // Le jour où le gouvernement redécoupe, une livraison de code suffit —
        // et c'est l'occasion de relire les validations qui en dépendent.
        //
        // ICI PLUTÔT QUE SUR LA PASSERELLE. Le BFF ne référence AUCUN projet
        // de `shared/` — trois projets, zéro référence, et c'est une frontière
        // délibérée. L'ouvrir pour une table statique la fermerait mal ensuite.
        // user-service est le porteur naturel : c'est lui qui possède les
        // adresses, et son domaine lit déjà `BeninGeography`.
        //
        // GROUPE DÉDIÉ ET ANONYME, HORS de `/api/users`. Le sélecteur de
        // commune s'affiche à l'INSCRIPTION, avant tout jeton : le poser dans un
        // groupe authentifié rendrait la création de compte impossible. Et ce
        // n'est pas une donnée d'utilisateur — la ranger sous `/api/users`
        // obligerait à l'expliquer à chaque lecture.
        // ═════════════════════════════════════════════════════════════════════
        var geo = app.MapGroup("/api/geo").WithTags("Géographie");

        geo.MapGet("/benin", BeninReference).AllowAnonymous().WithName("GetBeninGeography");

        return app;
    }

    /// <summary>Pays, indicatif, longueur de numéro, 12 départements, 77 communes.</summary>
    /// <remarks>
    /// UN SEUL APPEL, PAS UN PAR NIVEAU.
    ///
    /// Chaque commune porte déjà le nom de son département
    /// (<c>BeninCommuneView.DepartmentName</c>) : un écran qui affiche
    /// « Abomey-Calavi · Atlantique » n'a rien de plus à demander. Deux routes
    /// obligeraient chaque client à les joindre lui-même, et à traiter le cas où
    /// l'une répond et pas l'autre. La charge utile fait environ 8 Ko — moins
    /// qu'une seule photo de produit.
    ///
    /// `Cache-Control` PUBLIC, PARCE QUE LA DONNÉE EST CONSTANTE.
    ///
    /// Sans lui, chaque ouverture d'un formulaire d'adresse referait l'appel. Une
    /// journée est volontairement bien plus court que la réalité — des années :
    /// si une correction de libellé part un jour, elle se propage sans qu'on ait
    /// à purger quoi que ce soit.
    ///
    /// AUCUN `Result` ICI, ET C'EST NORMAL : il n'existe aucun cas d'échec.
    /// La valeur est une constante du programme, pas une lecture.
    /// </remarks>
    private static IResult BeninReference(HttpResponse response)
    {
        response.Headers.CacheControl = "public, max-age=86400";
        return Results.Ok(BeninGeography.Reference);
    }

    private static async Task<IResult> GetProfileAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new GetUserProfileQuery(userId), ct)).Match(profile => ApiResults.Ok(profile));

    private static async Task<IResult> RenameProfileAsync(
        ClaimsPrincipal user, RenameProfileRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new RenameUserProfileCommand(userId, request.FirstName, request.LastName), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> SetAvatarAsync(
        ClaimsPrincipal user, AvatarRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new SetUserAvatarCommand(userId, request.AvatarUrl), ct))
                .Match(() => Results.NoContent());

    /// <summary>
    /// PATCH /api/v1/users/me — nom et avatar en une seule requête (§10.2).
    /// Les champs absents ne sont pas touchés : un client qui n'envoie que
    /// `avatarUrl` ne doit pas effacer le nom, ce qu'un PUT aurait fait.
    /// </summary>
    private static async Task<IResult> UpdateProfileAsync(
        ClaimsPrincipal user, UpdateProfileRequest request, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Unauthenticated();
        }

        if (request.FirstName is not null || request.LastName is not null)
        {
            var rename = await sender.Send(
                new RenameUserProfileCommand(userId, request.FirstName, request.LastName), ct);

            if (rename.IsFailure)
            {
                return rename.Match(() => ApiResults.Ok(new { updated = false }));
            }
        }

        if (request.AvatarUrl is not null)
        {
            var avatar = await sender.Send(new SetUserAvatarCommand(userId, request.AvatarUrl), ct);

            if (avatar.IsFailure)
            {
                return avatar.Match(() => ApiResults.Ok(new { updated = false }));
            }
        }

        return (await sender.Send(new GetUserProfileQuery(userId), ct))
            .Match(profile => ApiResults.Ok(profile));
    }

    private static async Task<IResult> GetPreferencesAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new GetPreferencesQuery(userId), ct))
                .Match(preferences => ApiResults.Ok(preferences));

    private static async Task<IResult> UpdatePreferencesAsync(
        ClaimsPrincipal user, PreferencesRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new UpdatePreferencesCommand(
                    userId, request.Language, request.Currency,
                    request.PushEnabled, request.MarketingOptIn), ct))
                .Match(preferences => ApiResults.Ok(preferences));

    private static async Task<IResult> ListDevicesAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new ListDevicesQuery(userId), ct))
                .Match(devices => ApiResults.Ok(devices));

    private static async Task<IResult> RegisterDeviceAsync(
        ClaimsPrincipal user, DeviceRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new RegisterDeviceCommand(
                    userId, request.Platform, request.PushToken), ct))
                .Match(device => ApiResults.Created(device, $"/api/v1/users/me/devices/{device.Id}"));

    private static async Task<IResult> ListAddressesAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new ListAddressesQuery(userId), ct)).Match(addresses => ApiResults.Ok(addresses));

    private static async Task<IResult> AddAddressAsync(
        ClaimsPrincipal user, AddressRequest request, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Unauthenticated();
        }

        var result = await sender.Send(new AddAddressCommand(
            userId,
            request.Label,
            request.Recipient,
            request.Phone,
            request.Commune,
            request.Quartier,
            request.Landmark,
            request.Line1,
            request.Latitude,
            request.Longitude,
            request.MakeDefault), ct);

        return result.Match(id => ApiResults.Created(new { id }, $"/api/v1/users/me/addresses/{id}"));
    }

    private static async Task<IResult> UpdateAddressAsync(
        ClaimsPrincipal user, Guid id, AddressRequest request, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Unauthenticated();
        }

        var result = await sender.Send(new UpdateAddressCommand(
            userId,
            id,
            request.Label,
            request.Recipient,
            request.Phone,
            request.Commune,
            request.Quartier,
            request.Landmark,
            request.Line1,
            request.Latitude,
            request.Longitude,
            request.MakeDefault), ct);

        return result.Match(() => Results.NoContent());
    }

    private static async Task<IResult> DeleteAddressAsync(
        ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new DeleteAddressCommand(userId, id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetDefaultAddressAsync(
        ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Unauthenticated()
            : (await sender.Send(new SetDefaultAddressCommand(userId, id), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// 401 sous l'enveloppe du §5. `Results.Unauthorized()` rendait un corps VIDE :
    /// le client n'avait ni code à brancher ni `requestId` à citer, précisément sur
    /// l'erreur la plus fréquente en production — un jeton expiré.
    /// </summary>
    private static IResult Unauthenticated()
        => ApiResults.Failure(
            ErrorCodes.Unauthorized,
            "Authentification requise.",
            StatusCodes.Status401Unauthorized);

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record RenameProfileRequest(string? FirstName, string? LastName);

    /// <summary>Corps de `PATCH /api/v1/users/me`. Tout est optionnel : absent = inchangé.</summary>
    public sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? AvatarUrl);

    public sealed record AvatarRequest(string? AvatarUrl);

    /// <summary>Corps de `PUT /me/preferences`. Champ absent = préférence inchangée.</summary>
    public sealed record PreferencesRequest(
        string? Language, string? Currency, bool? PushEnabled, bool? MarketingOptIn);

    /// <summary>Corps de `POST /me/devices`.</summary>
    public sealed record DeviceRequest(string? Platform, string? PushToken);

    public sealed record AddressRequest(
        string? Label,
        string? Recipient,
        string? Phone,
        string? Commune,
        string? Quartier,
        string? Landmark,
        string? Line1,
        double? Latitude,
        double? Longitude,
        bool MakeDefault);
}
