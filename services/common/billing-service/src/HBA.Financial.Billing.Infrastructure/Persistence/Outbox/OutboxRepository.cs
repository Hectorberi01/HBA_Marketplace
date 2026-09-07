using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Retry;
using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Financial.Billing.Infrastructure.Persistence.Outbox;

/// <summary>
/// Implémenté par chaque DbContext de module : expose sa table d'outbox (dans son
/// propre schéma) afin que le publisher et le processeur soient génériques.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
