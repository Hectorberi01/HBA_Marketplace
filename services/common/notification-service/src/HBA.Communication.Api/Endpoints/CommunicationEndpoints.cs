using System.Security.Claims;
using HBA.Communication.Application.Conversations;
using HBA.Communication.Domain.Conversations;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Communication.Api.Endpoints;

/// <summary>Endpoints HTTP du service Communication, tranche messagerie.</summary>
public static class CommunicationEndpoints
{
    public static IEndpointRouteBuilder MapCommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        var messaging = app.MapAuthenticatedGroup("/api/notifications/messaging")
            .WithTags("Communication · Messaging");

        messaging.MapGet("/conversations", ListConversationsAsync).WithName("ListMyConversations");
        messaging.MapPost("/conversations", StartConversationAsync).WithName("StartConversation");
        messaging.MapGet("/conversations/{id:guid}", GetConversationAsync).WithName("GetConversation");
        messaging.MapPost("/conversations/{id:guid}/messages", SendMessageAsync).WithName("SendMessage");
        messaging.MapPut("/conversations/{id:guid}/read", MarkReadAsync).WithName("MarkConversationRead");
        messaging.MapPost("/conversations/{id:guid}/archive", ArchiveAsync).WithName("ArchiveConversation");
        messaging.MapPut("/conversations/{id:guid}/messages/{messageId:guid}/reaction", ReactAsync).WithName("ReactToMessage");
        messaging.MapDelete("/conversations/{id:guid}/messages/{messageId:guid}", DeleteForEveryoneAsync).WithName("DeleteMessageForEveryone");
        messaging.MapDelete("/conversations/{id:guid}/messages/{messageId:guid}/mine", HideForMeAsync).WithName("HideMessageForMe");

        return app;
    }

    private static async Task<IResult> ListConversationsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyConversationsQuery(userId), ct)).Match(items => Results.Ok(items));

    private static async Task<IResult> GetConversationAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new GetConversationQuery(id, userId), ct)).Match(item => Results.Ok(item));

    private static async Task<IResult> StartConversationAsync(
        ClaimsPrincipal user, StartConversationRequest request, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(
            new StartConversationCommand(userId, request.RecipientId, request.ContextType, request.ContextId, request.Message),
            ct);

        return result.Match(id => Results.Created($"/api/notifications/messaging/conversations/{id}", new { id }));
    }

    private static async Task<IResult> SendMessageAsync(
        Guid id, ClaimsPrincipal user, SendMessageRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new SendMessageCommand(id, userId, request.Body ?? string.Empty, request.Attachments ?? []), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> MarkReadAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new MarkConversationReadCommand(id, userId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ArchiveAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ArchiveConversationCommand(id, userId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ReactAsync(
        Guid id, Guid messageId, ClaimsPrincipal user, ReactionRequest request, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new ReactToMessageCommand(id, messageId, userId, request.Emoji), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteForEveryoneAsync(
        Guid id, Guid messageId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new DeleteMessageForEveryoneCommand(id, messageId, userId), ct))
                .Match(() => Results.NoContent());

    private static async Task<IResult> HideForMeAsync(
        Guid id, Guid messageId, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new HideMessageForMeCommand(id, messageId, userId), ct))
                .Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public sealed record StartConversationRequest(Guid RecipientId, string? ContextType, Guid? ContextId, string Message);

    public sealed record SendMessageRequest(string? Body, IReadOnlyList<MessageAttachmentInput>? Attachments);

    public sealed record ReactionRequest(string Emoji);
}
