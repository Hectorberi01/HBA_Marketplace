namespace HBA.Communication.Domain.Conversations;

public interface IConversationRepository
{
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conversations où l'utilisateur est participant (les plus récentes d'abord).
    /// </summary>
    /// <summary>
    /// Les conversations d'un utilisateur, de la plus récemment animée à la plus
    /// ancienne, dans la limite de <paramref name="take"/> .
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListByParticipantAsync(
        Guid userId, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>Cet utilisateur est-il participant de cette conversation ?</summary>
    Task<bool> IsParticipantAsync(ConversationId id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Ce média est-il joint à un message de CETTE conversation ?</summary>
    Task<bool> HasAttachmentAsync(ConversationId id, Guid mediaId, CancellationToken cancellationToken = default);
}
