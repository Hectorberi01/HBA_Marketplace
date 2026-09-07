using HBA.Shared.Domain.Primitives;

namespace HBA.Communication.Domain.Conversations;

/// <summary>Participant à une conversation.</summary>
public sealed class ConversationParticipant : Entity<Guid>
{
    private ConversationParticipant()
    {
    }

    internal ConversationParticipant(Guid id, Guid userId)
        : base(id)
    {
        UserId = userId;
    }

    public Guid UserId { get; private set; }
}
