using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// COPIE DEPUIS `HBA.Shared.Infrastructure.Audit`.

namespace HBA.FoodOrders.Infrastructure.Auditing;

/// <summary>Mapping EF du journal d'audit.</summary>
internal sealed class AuditConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");

        builder.HasKey(e => e.Id);

        // IDENTITÉ ET NON GUID.
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.EntityType).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Operation).HasConversion<int>().IsRequired();
        builder.Property(e => e.ActorUserId);
        builder.Property(e => e.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(100);
        builder.Property(e => e.OccurredOnUtc).IsRequired();

        // DEUX INDEX, ET DEUX QUESTIONS DISTINCTES.
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredOnUtc });
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredOnUtc });
    }
}
