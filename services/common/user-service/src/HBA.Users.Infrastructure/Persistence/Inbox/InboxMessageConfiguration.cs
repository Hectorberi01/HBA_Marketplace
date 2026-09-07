using HBA.Shared.Infrastructure.Persistence;
using HBA.Users.Infrastructure.Persistence;
using HBA.Users.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using HBA.Users.Infrastructure.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Inbox`.

namespace HBA.Users.Infrastructure.Persistence.Inbox;

/// <summary>Mapping EF de la table <c>consumer_inbox</c> (§19.5).</summary>
public sealed class ConsumerInboxConfiguration : IEntityTypeConfiguration<ConsumerInboxEntry>
{
    public void Configure(EntityTypeBuilder<ConsumerInboxEntry> builder)
    {
        builder.ToTable("consumer_inbox");

        // Clé composite : voir le commentaire de ConsumerInboxEntry.
        builder.HasKey(e => new { e.EventId, e.ConsumerName });

        builder.Property(e => e.ConsumerName).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(160).IsRequired();
        builder.Property(e => e.ProcessedAtUtc).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(100);

        // PURGE. La table grossit d'une ligne par message consommé, indéfiniment.
        builder.HasIndex(e => e.ProcessedAtUtc)
            .HasDatabaseName("ix_consumer_inbox_processed_at");
    }
}
