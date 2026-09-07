using HBA.Shared.Domain.Primitives;

namespace HBA.Communication.Domain.Conversations;

/// <summary>
/// Message d'une conversation, avec pièces jointes éventuelles (URLs object
/// storage), accusé de lecture, réactions et suppression.
/// </summary>
public sealed class Message : Entity<Guid>
{
    private readonly List<MessageAttachment> _attachments = new();
    private readonly List<MessageReaction> _reactions = new();
    private readonly List<MessageHiddenFor> _hiddenFor = new();

    private Message()
    {
    }

    internal Message(Guid id, Guid senderId, string body, IEnumerable<MessageAttachmentInput> attachments)
        : base(id)
    {
        SenderId = senderId;
        Body = body;

        // Chaque média devient une entité enfant, son type déduit du type MIME
        // réel.
        foreach (var piece in attachments.Where(a => a.MediaId != Guid.Empty))
        {
            _attachments.Add(new MessageAttachment(
                Guid.NewGuid(),
                piece.MediaId,
                legacyUrl: null,
                MessageAttachment.InferTypeFromContentType(piece.ContentType)));
        }

        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid SenderId { get; private set; }
    public string Body { get; private set; } = default!;

    /// <summary>Pièces jointes du message (média + type).</summary>
    public IReadOnlyCollection<MessageAttachment> Attachments => _attachments.AsReadOnly();

    public DateTime? ReadAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Date de suppression « pour tout le monde ».</summary>
    public DateTime? DeletedAtUtc { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    public IReadOnlyCollection<MessageReaction> Reactions => _reactions.AsReadOnly();
    public IReadOnlyCollection<MessageHiddenFor> HiddenFor => _hiddenFor.AsReadOnly();

    internal void MarkRead() => ReadAtUtc ??= DateTime.UtcNow;

    /// <summary>Suppression pour tout le monde.</summary>
    internal void DeleteForEveryone() => DeletedAtUtc ??= DateTime.UtcNow;

    /// <summary>Masque le message pour un utilisateur donné (« supprimer pour moi »).</summary>
    internal void HideFor(Guid userId)
    {
        if (_hiddenFor.All(h => h.UserId != userId))
        {
            _hiddenFor.Add(new MessageHiddenFor(Guid.NewGuid(), userId));
        }
    }

    public bool IsHiddenFor(Guid userId) => _hiddenFor.Any(h => h.UserId == userId);

    /// <summary>
    /// Applique une réaction : ajoute si l'utilisateur n'en a pas, la RETIRE si
    /// c'est le même emoji (bascule), la REMPLACE sinon.
    /// </summary>
    internal void React(Guid userId, string emoji)
    {
        var existing = _reactions.FirstOrDefault(r => r.UserId == userId);
        if (existing is null)
        {
            _reactions.Add(new MessageReaction(Guid.NewGuid(), userId, emoji));
            return;
        }

        if (string.Equals(existing.Emoji, emoji, StringComparison.Ordinal))
        {
            _reactions.Remove(existing); // même emoji → on retire (bascule)
            return;
        }

        existing.ChangeEmoji(emoji);
    }
}

/// <summary>
/// Marqueur « ce message est masqué pour cet utilisateur » (suppression pour moi).
/// </summary>
public sealed class MessageHiddenFor : Entity<Guid>
{
    private MessageHiddenFor()
    {
    }

    internal MessageHiddenFor(Guid id, Guid userId)
        : base(id)
    {
        UserId = userId;
        HiddenAtUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public DateTime HiddenAtUtc { get; private set; }
}
