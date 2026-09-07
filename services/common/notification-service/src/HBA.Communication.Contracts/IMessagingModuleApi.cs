namespace HBA.Communication.Contracts;

/// <summary>API in-process publique du module Messaging.</summary>
public interface IMessagingModuleApi
{
    /// <summary>Cet utilisateur est-il participant de cette conversation ?</summary>
    Task<bool> IsParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Participants d'une conversation.</summary>
    Task<IReadOnlyList<Guid>> ListParticipantsAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Ce média est-il joint à un message de cette conversation ?</summary>
    Task<bool> HasAttachmentAsync(Guid conversationId, Guid mediaId, CancellationToken cancellationToken = default);
}
