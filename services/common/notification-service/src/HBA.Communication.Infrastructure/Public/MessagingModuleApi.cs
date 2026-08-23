using HBA.Communication.Contracts;
using HBA.Communication.Domain.Conversations;

namespace HBA.Communication.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Messaging.</summary>
internal sealed class MessagingModuleApi : IMessagingModuleApi
{
    private readonly IConversationRepository _conversations;

    public MessagingModuleApi(IConversationRepository conversations)
        => _conversations = conversations;

    public Task<bool> IsParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default)
        => _conversations.IsParticipantAsync(new ConversationId(conversationId), userId, cancellationToken);

    public Task<bool> HasAttachmentAsync(Guid conversationId, Guid mediaId, CancellationToken cancellationToken = default)
        => _conversations.HasAttachmentAsync(new ConversationId(conversationId), mediaId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListParticipantsAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetByIdAsync(new ConversationId(conversationId), cancellationToken);
        return conversation is null ? Array.Empty<Guid>() : conversation.ParticipantIds.ToList();
    }
}
