using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using HBA.Communication.Notifications.Application.Devices;
using HBA.Communication.Notifications.Application.Notifications.Commands;
using HBA.Communication.Notifications.Application.Notifications.Preferences;
using HBA.Communication.Notifications.Application.Notifications.Queries;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Communication.Api.Endpoints;

/// <summary>
/// Endpoints HTTP de la tranche Notifications : boîte de réception, préférences,
/// jetons d'appareil.
/// </summary>
public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var inbox = app.MapAuthenticatedGroup("/api/notifications")
            .WithTags("Communication · Notifications");

        inbox.MapGet("/", ListAsync).WithName("ListMyNotifications");
        inbox.MapGet("/unread-count", UnreadCountAsync).WithName("GetUnreadNotificationCount");
        inbox.MapPost("/{id:guid}/read", MarkReadAsync).WithName("MarkNotificationRead");
        inbox.MapPost("/read-all", MarkAllReadAsync).WithName("MarkAllNotificationsRead");
        inbox.MapDelete("/{id:guid}", DeleteOwnAsync).WithName("DeleteOwnNotification");

        var preferences = app.MapAuthenticatedGroup("/api/notifications/preferences")
            .WithTags("Communication · Notifications");

        preferences.MapGet("/", GetPreferencesAsync).WithName("GetNotificationPreferences");
        preferences.MapPut("/", UpdatePreferencesAsync).WithName("UpdateNotificationPreferences");

        // CES DEUX ROUTES N'EXISTAIENT PAS DANS LE MONOLITHE.
        var devices = app.MapAuthenticatedGroup("/api/notifications/devices")
            .WithTags("Communication · Notifications");

        devices.MapPost("/", RegisterDeviceAsync).WithName("RegisterDeviceToken");
        devices.MapDelete("/", UnregisterDeviceAsync).WithName("UnregisterDeviceToken");

        return app;
    }

    // ── Boîte de réception ───────────────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct, int take = 50)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyNotificationsQuery(userId, take), ct))
                .Match(items => Results.Ok(items));

    private static async Task<IResult> UnreadCountAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetUnreadCountQuery(userId), ct))
                .Match(count => Results.Ok(new { count }));

    private static async Task<IResult> MarkReadAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            // Le destinataire fait partie de la commande : marquer lue la
            // notification d'un autre doit être impossible, pas seulement interdit.
            : (await sender.Send(new MarkNotificationReadCommand(id, userId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> MarkAllReadAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkAllNotificationsReadCommand(userId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteOwnAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeleteOwnNotificationCommand(id, userId), ct))
                .Match(() => Results.NoContent());

    // ── Préférences ──────────────────────────────────────────────────────────

    private static async Task<IResult> GetPreferencesAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            // `Results.Ok` en groupe de méthodes serait ambigu — deux surcharges,
            // `Ok()` et `Ok(object?)`.
            : (await sender.Send(new GetNotificationPreferencesQuery(userId), ct))
                .Match(preferences => Results.Ok(preferences));

    private static async Task<IResult> UpdatePreferencesAsync(
        UpdatePreferencesRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(
                new UpdateNotificationPreferencesCommand(userId, request.MutedCategories), ct))
                .Match(() => Results.NoContent());

    // ── Appareils ────────────────────────────────────────────────────────────

    private static async Task<IResult> RegisterDeviceAsync(
        RegisterDeviceRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(
                new RegisterDeviceTokenCommand(userId, request.Token, request.Platform), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> UnregisterDeviceAsync(
        [FromBody] UnregisterDeviceRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new UnregisterDeviceTokenCommand(userId, request.Token), ct))
                .Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record UpdatePreferencesRequest(IReadOnlyList<string> MutedCategories);

    /// <param name="Platform">« android », « ios » ou « web ».</param>
    public sealed record RegisterDeviceRequest(string Token, string Platform);

    public sealed record UnregisterDeviceRequest(string Token);
}
