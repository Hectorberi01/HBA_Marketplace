using HBA.Shared.Domain.Primitives;

namespace HBA.Communication.Domain.Conversations;

/// <summary>
/// Participant à une conversation. Modélisé en entité enfant (et non en tableau)
/// pour rester requêtable : « mes conversations » filtre sur cette table.
/// </summary>
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
