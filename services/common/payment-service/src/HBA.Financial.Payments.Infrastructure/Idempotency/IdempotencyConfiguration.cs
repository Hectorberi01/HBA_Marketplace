using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Payments.Infrastructure.Persistence.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.
//
// La table `idempotency_records` de CE service est creee par SES migrations :
// l'entite qui la decrit lui appartient. Le socle n'en garde que le port,
// `IIdempotencyStore`, que `IdempotencyEndpointFilter` resout sur chaque route
// annotee `AllowIdempotency()`.
//
// A REGENERER : l'instantane de modele de ce service reference encore le type du
// socle sous forme de chaine. Il compile et les migrations s'appliquent — mais
// modele et instantane divergent jusqu'a un `dotnet ef migrations add`, au diff
// de schema vide.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Financial.Payments.Infrastructure.Idempotency;

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
