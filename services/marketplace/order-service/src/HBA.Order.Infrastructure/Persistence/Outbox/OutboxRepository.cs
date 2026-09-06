using HBA.Shared.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Persistence.Inbox;
using Microsoft.EntityFrameworkCore;

using HBA.Orders.Infrastructure.Messaging.Kafka.Retry;
using HBA.Orders.Infrastructure.Messaging.Kafka.Processors;
// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.
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

namespace HBA.Orders.Infrastructure.Persistence.Outbox;

/// <summary>
/// Implémenté par chaque DbContext de module : expose sa table d'outbox (dans
/// son propre schéma) afin que le publisher et le processeur soient génériques.
/// </summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
