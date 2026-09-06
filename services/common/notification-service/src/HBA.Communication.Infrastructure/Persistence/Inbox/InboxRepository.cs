using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Infrastructure.Persistence;
using HBA.Communication.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

using HBA.Shared.Infrastructure.Events;
using HBA.Communication.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Infrastructure.Messaging.Kafka.Processors;
// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Inbox`.
//
// L'outbox et l'inbox appartiennent au service : leurs tables sont creees par SES
// migrations. `shared` n'en garde que les ports — `IConsumerInbox`, la file en
// memoire, et deux marqueurs vides sans lesquels le journal d'audit se
// journaliserait lui-meme.
//
// CE QUE CETTE COPIE COUTE, ET IL FAUT LE SAVOIR : c'est le chemin qui garantit
// qu'aucun evenement n'est perdu. Il existe maintenant en un exemplaire par
// service. Un defaut corrige ici ne l'est nulle part ailleurs.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Communication.Infrastructure.Persistence.Inbox;

/// <summary>
/// Implémentation EF de <see cref="IConsumerInbox"/>, générique sur le DbContext du
/// service. Le service enregistre <c>IConsumerInbox -> EfConsumerInbox&lt;MonDbContext&gt;</c>
/// et applique <see cref="ConsumerInboxConfiguration"/> dans son <c>OnModelCreating</c>.
///
/// <see cref="MarkProcessedAsync"/> n'appelle PAS <c>SaveChangesAsync</c>. C'est
/// délibéré et c'est tout l'intérêt : la trace doit être committée par la MÊME
/// transaction que l'effet métier. Un SaveChanges ici créerait deux transactions
/// distinctes, et la fenêtre entre les deux est exactement le trou que l'inbox est
/// censée fermer.
/// </summary>
public sealed class EfConsumerInbox : IConsumerInbox
{
    private readonly MessagingDbContext _context;

    public EfConsumerInbox(MessagingDbContext context) => _context = context;

    public Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken = default)
        => _context.Set<ConsumerInboxEntry>()
            .AsNoTracking()
            .AnyAsync(e => e.EventId == eventId && e.ConsumerName == consumerName, cancellationToken);

    public Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        string eventType,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        _context.Set<ConsumerInboxEntry>().Add(new ConsumerInboxEntry
        {
            EventId = eventId,
            ConsumerName = consumerName,
            EventType = eventType,
            CorrelationId = correlationId,
            ProcessedAtUtc = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }
}
