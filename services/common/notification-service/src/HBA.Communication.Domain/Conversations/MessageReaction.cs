using HBA.Shared.Domain.Primitives;

namespace HBA.Communication.Domain.Conversations;

/// <summary>Réaction d'un participant à un message (emoji).</summary>
public sealed class MessageReaction : Entity<Guid>
{
    private MessageReaction()
    {
    }

    internal MessageReaction(Guid id, Guid userId, string emoji)
        : base(id)
    {
        UserId = userId;
        Emoji = emoji;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Emoji { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    internal void ChangeEmoji(string emoji)
    {
        Emoji = emoji;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Palette d'emojis autorisés.</summary>
public static class MessageReactions
{
    public static readonly IReadOnlyList<string> Allowed = new[] { "👍", "❤️", "😂", "😮", "😢", "🙏" };

    public static bool IsAllowed(string? emoji)
        => !string.IsNullOrWhiteSpace(emoji) && Allowed.Contains(emoji);
}
