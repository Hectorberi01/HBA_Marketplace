namespace HBA.Communication.Contracts;

/// <summary>
/// Réaction agrégée sur un message : l'emoji, combien de personnes l'ont mis, et si
/// le lecteur courant en fait partie (pour afficher la pastille en surbrillance).
/// </summary>
public sealed record MessageReactionSummary(string Emoji, int Count, bool Mine);

/// <summary>
/// Message d'une conversation, projeté POUR UN LECTEUR donné.
/// <para>
/// <c>IsDeleted</c> = supprimé pour tout le monde : le <c>Body</c> renvoyé vaut alors
/// « Message supprimé » (le corps réel reste en base, accessible au support).
/// Les messages masqués par le lecteur (« supprimer pour moi ») ne sont pas renvoyés du tout.
/// </para>
/// </summary>
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

/// <summary>
/// Une pièce jointe, vue de l'extérieur.
///
/// AUCUNE URL POUR LES PIÈCES RÉCENTES, ET C'EST VOULU.
///
/// Le client obtient l'identifiant, puis demande une URL signée de courte durée à
/// la route gardée de la conversation. C'est ce détour qui permet de vérifier
/// qu'il est bien partie à la discussion — vérification que l'ancienne URL
/// publique permanente rendait impossible.
///
/// <paramref name="LegacyUrl"/> n'est renseignée que pour les pièces d'avant la
/// bascule, dont les octets vivent encore dans un bucket public.
/// </summary>
public sealed record MessageAttachmentSummary(Guid MediaId, string Type, string? LegacyUrl);
