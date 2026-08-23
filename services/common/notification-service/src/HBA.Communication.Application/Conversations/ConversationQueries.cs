using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Communication.Contracts;
using HBA.Communication.Domain.Conversations;

namespace HBA.Communication.Application.Conversations;

/// <summary>Récupère une conversation (le demandeur doit en être participant).</summary>
public sealed record GetConversationQuery(Guid ConversationId, Guid RequesterId) : IQuery<ConversationSummary>;

/// <summary>Liste les conversations de l'utilisateur.</summary>
/// <param name="Take">Combien de conversations au plus. Plafonné côté serveur.</param>
public sealed record ListMyConversationsQuery(Guid UserId, int Take = 50)
    : IQuery<IReadOnlyList<ConversationSummary>>;

internal static class ConversationMapper
{
    /// <summary>Corps affiché à la place d'un message supprimé pour tout le monde.</summary>
    private const string DeletedPlaceholder = "Message supprimé";

    /// <summary>
    /// Projette une conversation POUR UN LECTEUR donné. C'est ici — et nulle part
    /// ailleurs — que s'applique la confidentialité :
    /// <list type="bullet">
    ///   <item>les messages que le lecteur a masqués (« supprimer pour moi ») sont
    ///         purement et simplement absents de sa vue ;</item>
    ///   <item>les messages supprimés pour tout le monde voient leur corps ET leurs
    ///         pièces jointes remplacés par un marqueur — le contenu réel reste en base
    ///         pour le support et la preuve, mais ne sort jamais par cette API ;</item>
    ///   <item>les réactions sont agrégées par emoji, avec un drapeau « Mine ».</item>
    /// </list>
    /// </summary>
    public static ConversationSummary ToSummary(Conversation c, Guid viewerId) => new(
        c.Id.Value, c.ParticipantIds.ToList(), c.ContextType, c.ContextId, c.Status.ToString(), c.LastMessageAtUtc,
        c.Messages
            .Where(m => !m.IsHiddenFor(viewerId))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new MessageSummary(
                m.Id,
                m.SenderId,
                m.IsDeleted ? DeletedPlaceholder : m.Body,
                // AUCUNE URL POUR LES PIÈCES RÉCENTES : le client demande un lien
                // signé à la route gardée, qui vérifie qu'il est partie à la
                // conversation.
                //
                // MAIS `LegacyUrl` SORT ENCORE, ET LA FUITE RESTE OUVERTE POUR
                // LES PIÈCES D'AVANT LA BASCULE.
                //
                // Leurs octets sont dans un bucket public et la migration ne les a
                // pas déplacés : masquer l'adresse ici les rendrait simplement
                // invisibles dans l'application, sans rien fermer — elles resteraient
                // lisibles par leur URL, que les clients ont déjà. Le correctif
                // complet suppose de recopier ces fichiers vers le stockage privé
                // puis d'effacer les originaux. Tant que ce n'est pas fait, ce champ
                // dit la vérité sur l'état réel du système.
                m.IsDeleted
                    ? Array.Empty<MessageAttachmentSummary>()
                    : m.Attachments
                        .Select(a => new MessageAttachmentSummary(a.MediaId, a.Type.ToString(), a.LegacyUrl))
                        .ToList(),
                m.ReadAtUtc,
                m.CreatedAtUtc,
                m.IsDeleted,
                m.IsDeleted
                    ? Array.Empty<MessageReactionSummary>()
                    : m.Reactions
                        .GroupBy(r => r.Emoji, StringComparer.Ordinal)
                        .Select(g => new MessageReactionSummary(g.Key, g.Count(), g.Any(r => r.UserId == viewerId)))
                        .ToList()))
            .ToList());
}

internal sealed class GetConversationQueryHandler : IQueryHandler<GetConversationQuery, ConversationSummary>
{
    private readonly IConversationRepository _repository;
    public GetConversationQueryHandler(IConversationRepository repository) => _repository = repository;

    public async Task<Result<ConversationSummary>> Handle(GetConversationQuery query, CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetByIdAsync(new ConversationId(query.ConversationId), cancellationToken);
        if (conversation is null)
        {
            return Error.NotFound("messaging.not_found", "Conversation introuvable.");
        }

        if (!conversation.ParticipantIds.Contains(query.RequesterId))
        {
            return Error.Forbidden("messaging.not_participant", "Vous n'êtes pas participant à cette conversation.");
        }

        return ConversationMapper.ToSummary(conversation, query.RequesterId);
    }
}

internal sealed class ListMyConversationsQueryHandler : IQueryHandler<ListMyConversationsQuery, IReadOnlyList<ConversationSummary>>
{
    private const int PlafondDeLecture = 200;

    private readonly IConversationRepository _repository;
    public ListMyConversationsQueryHandler(IConversationRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ConversationSummary>>> Handle(ListMyConversationsQuery query, CancellationToken cancellationToken)
    {
        // PLAFOND SERVEUR : un `Take` venu du client ne doit pas pouvoir rouvrir
        // le chargement intégral de la messagerie que ce lot ferme.
        var borne = query.Take <= 0 ? 50 : Math.Min(query.Take, PlafondDeLecture);

        var items = await _repository.ListByParticipantAsync(query.UserId, borne, cancellationToken);
        IReadOnlyList<ConversationSummary> summaries = items.Select(c => ConversationMapper.ToSummary(c, query.UserId)).ToList();
        return Result.Success(summaries);
    }
}
