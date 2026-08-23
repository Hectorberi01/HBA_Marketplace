using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Shared.Infrastructure.Idempotency;

/// <summary>Mapping EF de la table <c>idempotency_keys</c>, locale au service.</summary>
public sealed class IdempotencyConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(r => new { r.Key, r.Scope, r.Endpoint });

        builder.Property(r => r.Key).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Scope).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Endpoint).HasMaxLength(200).IsRequired();
        builder.Property(r => r.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(r => r.StatusCode).IsRequired();
        builder.Property(r => r.ResponseBody).HasColumnType("jsonb");
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.ExpiresAtUtc).IsRequired();

        // Index de purge. Partiel volontairement absent ici : contrairement à l'outbox,
        // TOUTES les lignes finissent par expirer, donc un filtre n'écarterait rien.
        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasDatabaseName("ix_idempotency_keys_expires_at");
    }
}
