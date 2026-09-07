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
        var users = app.MapAuthenticatedGroup("/api/v1/users").WithTags("Users");

        users.MapGet("/me", GetProfileAsync).WithName("GetMyUserProfile");

        // §10.2 : « PATCH /api/v1/users/me — met à jour les champs éditables ».
        users.MapPatch("/me", UpdateProfileAsync).WithName("UpdateMyUserProfile").AllowIdempotency();
        users.MapGet("/me/profile", GetProfileAsync).WithName("GetMyUserProfileDetails");
        users.MapPatch("/me/profile", RenameProfileAsync).WithName("RenameMyUserProfile");

        users.MapGet("/me/avatar", GetProfileAsync).WithName("GetMyUserAvatar");
        users.MapPut("/me/avatar", SetAvatarAsync).WithName("SetMyUserAvatar");

        // §10.2 : préférences et appareils.
        users.MapGet("/me/preferences", GetPreferencesAsync).WithName("GetMyPreferences");
        users.MapPut("/me/preferences", UpdatePreferencesAsync).WithName("UpdateMyPreferences").AllowIdempotency();

        users.MapGet("/me/devices", ListDevicesAsync).WithName("ListMyDevices");
        users.MapPost("/me/devices", RegisterDeviceAsync).WithName("RegisterMyDevice").RequireIdempotency();

        users.MapGet("/me/addresses", ListAddressesAsync).WithName("ListMyAddresses");
        // §5 : `Idempotency-Key` obligatoire sur les POST de création.
        users.MapPost("/me/addresses", AddAddressAsync).WithName("AddMyAddress").RequireIdempotency();
        users.MapPut("/me/addresses/{id:guid}", UpdateAddressAsync).WithName("UpdateMyAddress");
        users.MapDelete("/me/addresses/{id:guid}", DeleteAddressAsync).WithName("DeleteMyAddress");
        users.MapPut("/me/addresses/{id:guid}/default", SetDefaultAddressAsync).WithName("SetMyDefaultAddress");

        // LE RÉFÉRENTIEL GÉOGRAPHIQUE — IL EXISTAIT, PERSONNE NE LE PUBLIAIT.
        var geo = app.MapGroup("/api/geo").WithTags("Géographie");

        geo.MapGet("/benin", BeninReference).AllowAnonymous().WithName("GetBeninGeography");

        return app;
    }

    /// <summary>Pays, indicatif, longueur de numéro, 12 départements, 77 communes.</summary>
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

    /// <summary>PATCH /api/v1/users/me — nom et avatar en une seule requête (§10.2).</summary>
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

    /// <summary>401 sous l'enveloppe du §5.</summary>
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

    /// <summary>Corps de `PATCH /api/v1/users/me`.</summary>
    public sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? AvatarUrl);

    public sealed record AvatarRequest(string? AvatarUrl);

    /// <summary>Corps de `PUT /me/preferences`.</summary>
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
