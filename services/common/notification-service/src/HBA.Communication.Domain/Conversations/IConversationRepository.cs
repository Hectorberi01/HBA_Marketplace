namespace HBA.Communication.Domain.Conversations;

public interface IConversationRepository
{
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken = default);

    /// <summary>Conversations où l'utilisateur est participant (les plus récentes d'abord).</summary>
    /// <summary>
    /// Les conversations d'un utilisateur, de la plus récemment animée à la plus
    /// ancienne, dans la limite de <paramref name="take"/>.
    /// </summary>
    /// <remarks>
    /// LA BORNE EST NOUVELLE, ET ELLE ÉTAIT LA PLUS URGENTE DU LOT (§12).
    ///
    /// Cette méthode chargeait TOUTES les conversations d'un utilisateur, avec tous
    /// leurs messages, et pour chaque message ses pièces jointes, ses réactions et
    /// ses masquages. À chaque ouverture de la messagerie. Le coût croissait avec
    /// l'ancienneté du compte, sans aucun plafond — un utilisateur fidèle était
    /// puni de sa fidélité.
    ///
    /// `AsSplitQuery` était déjà là : il divise le nombre de LIGNES SQL, pas le
    /// volume rapatrié. Ce n'était pas une borne.
    /// </remarks>
    Task<IReadOnlyList<Conversation>> ListByParticipantAsync(
        Guid userId, int take = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet utilisateur est-il participant de cette conversation ?
    ///
    /// EXISTS pur : ni Include, ni matérialisation. `GetByIdAsync` chargerait la
    /// conversation ENTIÈRE — tous les messages, toutes les réactions, tous les masquages —
    /// pour ne répondre qu'à une question booléenne, et à chaque ouverture de fil.
    /// </summary>
    Task<bool> IsParticipantAsync(ConversationId id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ce média est-il joint à un message de CETTE conversation ?
    ///
    /// SANS CETTE QUESTION, LE CONTRÔLE DE PARTICIPATION NE PROTÈGE RIEN.
    ///
    /// La route gardée vérifie que le demandeur participe à la conversation
    /// {id} — puis signerait le média qu'il nomme. Il suffirait alors d'ouvrir une
    /// conversation avec n'importe qui, puis d'y réclamer le média d'une AUTRE
    /// discussion : le contrôle porterait sur la bonne conversation et le mauvais
    /// fichier. C'est exactement le défaut trouvé sur les pièces KYB.
    ///
    /// EXISTS sur index, comme la participation.
    /// </summary>
    Task<bool> HasAttachmentAsync(ConversationId id, Guid mediaId, CancellationToken cancellationToken = default);
}
