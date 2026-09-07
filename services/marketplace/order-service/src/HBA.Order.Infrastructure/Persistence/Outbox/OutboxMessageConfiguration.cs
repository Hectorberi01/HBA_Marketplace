using HBA.Shared.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Persistence.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using HBA.Orders.Infrastructure.Messaging.Kafka.Retry;
using HBA.Orders.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Orders.Infrastructure.Persistence.Outbox;

/// <summary>Mapping EF de la table outbox.</summary>
public sealed class OutboxConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(500).IsRequired();
        builder.Property(m => m.Content).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.OccurredOnUtc).IsRequired();
        builder.Property(m => m.ProcessedOnUtc);
        builder.Property(m => m.Error);

        // Réessais. Valeurs par défaut choisies pour que les lignes DÉJÀ EN BASE se
        // comportent exactement comme avant : 0 tentative, aucune temporisation,
        // aucune lettre morte.
        builder.Property(m => m.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.NextAttemptAtUtc);
        builder.Property(m => m.DeadLetteredOnUtc);

        // 55 caractères suffisent au format W3C `00-<32>-<16>-<2>`, qui en fait 55.
        builder.Property(m => m.TraceParent).HasMaxLength(64);

        // 100 caractères : la même borne que `consumer_inbox.CorrelationId` et que
        // la colonne d'audit.
        builder.Property(m => m.CorrelationId).HasMaxLength(100);

        // INDEX DE LA FILE ÉLIGIBLE.
        builder.HasIndex(m => new { m.NextAttemptAtUtc, m.OccurredOnUtc })
            .HasFilter("\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL")
            .HasDatabaseName("ix_outbox_messages_pending");

        // Les lettres mortes sont rares, mais doivent se lister instantanément dans
        // la console d'admin.
        builder.HasIndex(m => m.DeadLetteredOnUtc)
            .HasFilter("\"DeadLetteredOnUtc\" IS NOT NULL")
            .HasDatabaseName("ix_outbox_messages_dead_letters");
    }
}
