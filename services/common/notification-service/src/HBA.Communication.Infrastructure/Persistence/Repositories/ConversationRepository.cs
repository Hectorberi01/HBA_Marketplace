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
            // Les collections enfants des messages DOIVENT être chargées : sans
            // elles, la projection verrait zéro réaction, zéro pièce jointe et
            // aucun masquage (« supprimer pour moi » n'aurait plus aucun effet
            // visible).
            .Include(c => c.Messages).ThenInclude(m => m.Attachments)
            .Include(c => c.Messages).ThenInclude(m => m.Reactions)
            .Include(c => c.Messages).ThenInclude(m => m.HiddenFor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByParticipantAsync(
        Guid userId, int take = 50, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations
            .Where(c => c.Participants.Any(p => p.UserId == userId))
            .OrderByDescending(c => c.LastMessageAtUtc)
            .Take(take <= 0 ? 50 : take)
            .Include(c => c.Participants)
            // Les collections enfants des messages DOIVENT être chargées : sans
            // elles, la projection verrait zéro réaction, zéro pièce jointe et
            // aucun masquage.
            .Include(c => c.Messages).ThenInclude(m => m.Attachments)
            .Include(c => c.Messages).ThenInclude(m => m.Reactions)
            .Include(c => c.Messages).ThenInclude(m => m.HiddenFor)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<bool> IsParticipantAsync(ConversationId id, Guid userId, CancellationToken cancellationToken = default)
        // Pas d'Include, pas de ToList : un EXISTS. Cette question est posée à
        // chaque ouverture de conversation dans l'app — elle ne doit rien coûter.
        => await _dbContext.Conversations
            .AnyAsync(c => c.Id == id && c.Participants.Any(p => p.UserId == userId), cancellationToken);

    public async Task<bool> HasAttachmentAsync(
        ConversationId id, Guid mediaId, CancellationToken cancellationToken = default)
        // ON N'ÉCARTE PAS LES MESSAGES SUPPRIMÉS.
        => await _dbContext.Conversations
            .AnyAsync(
                c => c.Id == id && c.Messages.Any(m => m.Attachments.Any(a => a.MediaId == mediaId)),
                cancellationToken);
}
