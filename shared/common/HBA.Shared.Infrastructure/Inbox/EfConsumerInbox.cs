using Microsoft.EntityFrameworkCore;

namespace HBA.Shared.Infrastructure.Inbox;

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
public sealed class EfConsumerInbox<TContext> : IConsumerInbox
    where TContext : DbContext
{
    private readonly TContext _context;

    public EfConsumerInbox(TContext context) => _context = context;

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
