using Microsoft.EntityFrameworkCore;
using HBA.Communication.Domain.Conversations;

namespace HBA.Communication.Infrastructure.Persistence;

internal sealed class ConversationRepository : IConversationRepository
{
    private readonly MessagingDbContext _dbContext;

    public ConversationRepository(MessagingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations.AddAsync(conversation, cancellationToken);

    public async Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations
            .Include(c => c.Participants)
            // Les collections enfants des messages DOIVENT être chargées : sans elles,
            // la projection verrait zéro réaction, zéro pièce jointe et aucun masquage
            // (« supprimer pour moi » n'aurait plus aucun effet visible).
            //
            // AsSplitQuery : avec PLUSIEURS collections `.Include` (attachments +
            // reactions + hiddenFor), une requête unique produit un cartésien qu'EF Core 8
            // bufferise mal — certaines collections reviennent VIDES. On charge donc chaque
            // collection dans sa propre requête. C'était la vraie cause du bug des pièces
            // jointes (la 3ᵉ collection incluse).
            .Include(c => c.Messages).ThenInclude(m => m.Attachments)
            .Include(c => c.Messages).ThenInclude(m => m.Reactions)
            .Include(c => c.Messages).ThenInclude(m => m.HiddenFor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <remarks>
    /// LE `Take` EST POSÉ AVANT LES `Include`, ET C'EST DÉLIBÉRÉ À LA LECTURE.
    ///
    /// EF applique de toute façon la limite à la requête racine — les `Include` ne
    /// multiplient pas les conversations rapatriées. Mais l'ordre d'écriture dit ce
    /// que la méthode fait : on prend N conversations, PUIS on charge leur contenu.
    /// L'écriture inverse laissait croire que la limite portait sur le tout.
    ///
    /// CE QUI N'EST TOUJOURS PAS BORNÉ : le contenu d'UNE conversation. Un fil de
    /// dix mille messages remonte encore entier, avec ses pièces jointes. Borner le
    /// nombre de conversations divise le pire cas, il ne le supprime pas — il
    /// faudrait une pagination des MESSAGES, qui change la projection rendue au
    /// client.
    /// </remarks>
    public async Task<IReadOnlyList<Conversation>> ListByParticipantAsync(
        Guid userId, int take = 50, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations
            .Where(c => c.Participants.Any(p => p.UserId == userId))
            .OrderByDescending(c => c.LastMessageAtUtc)
            .Take(take <= 0 ? 50 : take)
            .Include(c => c.Participants)
            // Les collections enfants des messages DOIVENT être chargées : sans elles,
            // la projection verrait zéro réaction, zéro pièce jointe et aucun masquage.
            .Include(c => c.Messages).ThenInclude(m => m.Attachments)
            .Include(c => c.Messages).ThenInclude(m => m.Reactions)
            .Include(c => c.Messages).ThenInclude(m => m.HiddenFor)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<bool> IsParticipantAsync(ConversationId id, Guid userId, CancellationToken cancellationToken = default)
        // Pas d'Include, pas de ToList : un EXISTS. Cette question est posée à chaque
        // ouverture de conversation dans l'app — elle ne doit rien coûter.
        => await _dbContext.Conversations
            .AnyAsync(c => c.Id == id && c.Participants.Any(p => p.UserId == userId), cancellationToken);

    public async Task<bool> HasAttachmentAsync(
        ConversationId id, Guid mediaId, CancellationToken cancellationToken = default)
        // ON N'ÉCARTE PAS LES MESSAGES SUPPRIMÉS.
        //
        // Un message « supprimé pour tous » est masqué de l'affichage, mais la
        // pièce jointe qu'il portait a été vue par l'autre partie. Refuser d'en
        // signer l'accès reviendrait à réécrire l'historique d'un litige — et
        // c'est précisément l'inverse de ce qu'on attend de ces fichiers.
        => await _dbContext.Conversations
            .AnyAsync(
                c => c.Id == id && c.Messages.Any(m => m.Attachments.Any(a => a.MediaId == mediaId)),
                cancellationToken);
}
