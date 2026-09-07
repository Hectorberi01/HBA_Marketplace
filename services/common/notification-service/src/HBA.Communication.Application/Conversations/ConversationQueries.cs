using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Communication.Contracts;
using HBA.Communication.Domain.Conversations;

namespace HBA.Communication.Application.Conversations;

/// <summary>Récupère une conversation (le demandeur doit en être participant).</summary>
public sealed record GetConversationQuery(Guid ConversationId, Guid RequesterId) : IQuery<ConversationSummary>;

/// <summary>Liste les conversations de l'utilisateur.</summary>
/// <param name="Take">Combien de conversations au plus.</param>
public sealed record ListMyConversationsQuery(Guid UserId, int Take = 50)
    : IQuery<IReadOnlyList<ConversationSummary>>;

internal static class ConversationMapper
{
    /// <summary>Corps affiché à la place d'un message supprimé pour tout le monde.</summary>
    private const string DeletedPlaceholder = "Message supprimé";

    /// <summary>Projette une conversation POUR UN LECTEUR donné.</summary>
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
        // PLAFOND SERVEUR : un `Take` venu du client ne doit pas pouvoir rouvrir le
        // chargement intégral de la messagerie que ce lot ferme.
        var borne = query.Take <= 0 ? 50 : Math.Min(query.Take, PlafondDeLecture);

        var items = await _repository.ListByParticipantAsync(query.UserId, borne, cancellationToken);
        IReadOnlyList<ConversationSummary> summaries = items.Select(c => ConversationMapper.ToSummary(c, query.UserId)).ToList();
        return Result.Success(summaries);
    }
}
