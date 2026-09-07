namespace HBA.Communication.Contracts;

/// <summary>
/// Réaction agrégée sur un message : l'emoji, combien de personnes l'ont mis, et si
/// le lecteur courant en fait partie (pour afficher la pastille en surbrillance).
/// </summary>
public sealed record MessageReactionSummary(string Emoji, int Count, bool Mine);

/// <summary>Message d'une conversation, projeté POUR UN LECTEUR donné.</summary>
public sealed record MessageSummary(
    Guid Id,
    Guid SenderId,
    string Body,
    IReadOnlyList<MessageAttachmentSummary> Attachments,
    DateTime? ReadAtUtc,
    DateTime CreatedAtUtc,
    bool IsDeleted,
    IReadOnlyList<MessageReactionSummary> Reactions);

/// <summary>Vue publique d'une conversation (projetée pour un lecteur).</summary>
public sealed record ConversationSummary(
    Guid Id,
    IReadOnlyList<Guid> ParticipantIds,
    string? ContextType,
    Guid? ContextId,
    string Status,
    DateTime LastMessageAtUtc,
    IReadOnlyList<MessageSummary> Messages);

/// <summary>Une pièce jointe, vue de l'extérieur.</summary>
public sealed record MessageAttachmentSummary(Guid MediaId, string Type, string? LegacyUrl);
