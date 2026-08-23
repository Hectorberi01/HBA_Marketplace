using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Shared.Infrastructure.Inbox;

/// <summary>
/// Mapping EF de la table <c>consumer_inbox</c> (§19.5). Chaque service l'applique
/// dans son propre schéma, comme l'outbox : une inbox partagée entre services
/// recréerait la base commune que le §9 interdit.
/// </summary>
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

        // ─────────────────────────────────────────────────────────────────────────
        // PURGE. La table grossit d'une ligne par message consommé, indéfiniment.
        //
        // Elle n'est jamais lue autrement que par sa clé primaire, donc sa taille ne
        // ralentit pas le chemin chaud — mais elle occupe du disque et allonge les
        // sauvegardes. L'index sur la date sert uniquement au travail de purge, qui
        // doit conserver au moins la fenêtre de rétention Kafka du topic : effacer
        // une trace plus tôt que le message qu'elle protège rouvrirait la porte au
        // double traitement au premier rejeu.
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(e => e.ProcessedAtUtc)
            .HasDatabaseName("ix_consumer_inbox_processed_at");
    }
}
