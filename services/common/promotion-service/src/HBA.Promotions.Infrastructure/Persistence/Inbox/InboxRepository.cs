using HBA.Shared.Infrastructure.Persistence;
using HBA.Promotions.Infrastructure.Persistence;
using HBA.Promotions.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

using HBA.Shared.Infrastructure.Events;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Retry;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Inbox`.

namespace HBA.Promotions.Infrastructure.Persistence.Inbox;

/// <summary>
/// Implémentation EF de <see cref="IConsumerInbox"/> , générique sur le DbContext
/// du service.
/// </summary>
public sealed class EfConsumerInbox : IConsumerInbox
{
    private readonly PromotionsDbContext _context;

    public EfConsumerInbox(PromotionsDbContext context) => _context = context;

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
