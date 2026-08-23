using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Shared.Infrastructure.Audit;

/// <summary>
/// Mapping EF du journal d'audit. Chaque module qui l'active l'applique dans son
/// PROPRE schéma — même règle que l'outbox : pas de table partagée entre modules,
/// donc pas de dépendance croisée à démêler le jour d'une extraction.
/// </summary>
public sealed class AuditConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");

        builder.HasKey(e => e.Id);

        // ═════════════════════════════════════════════════════════════════════
        // IDENTITÉ ET NON GUID.
        //
        // Cette table est APPEND-ONLY et se lit dans l'ordre chronologique. Un GUID
        // v4 en clé primaire ferait éclater les insertions dans tout l'index
        // B-tree — sur la table la plus écrite du schéma, c'est de la fragmentation
        // offerte, et sans contrepartie : personne ne cite une ligne d'audit par
        // son identifiant.
        //
        // `ValueGeneratedOnAdd` ET NON `UseIdentityByDefaultColumn`.
        //
        // La seconde est une extension Npgsql, et `HBA.Shared.Infrastructure` ne
        // référence PAS le fournisseur PostgreSQL — délibérément : c'est le projet
        // que tous les modules partagent, et y faire entrer un fournisseur de base
        // le rendrait inutilisable pour un test en mémoire. Le fournisseur Npgsql
        // traduit déjà `ValueGeneratedOnAdd` sur un `long` en colonne d'identité ;
        // c'est la même chose écrite sans la dépendance. Même raisonnement que
        // `ConcurrencyTokenExtensions` pour `xmin`.
        // ═════════════════════════════════════════════════════════════════════
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.EntityType).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Operation).HasConversion<int>().IsRequired();
        builder.Property(e => e.ActorUserId);
        builder.Property(e => e.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(100);
        builder.Property(e => e.OccurredOnUtc).IsRequired();

        // ─────────────────────────────────────────────────────────────────────
        // DEUX INDEX, ET DEUX QUESTIONS DISTINCTES.
        //
        //   « qu'est-il arrivé à CETTE fiche »  → (EntityType, EntityId, date)
        //   « qu'a fait CE membre »             → (ActorUserId, date)
        //
        // Ce sont les deux seules qu'on pose devant un litige, et aucune ne se
        // sert de l'index de l'autre. En ajouter un troisième « au cas où »
        // ralentirait chaque écriture — c'est-à-dire chaque mutation du service.
        // ─────────────────────────────────────────────────────────────────────
        builder.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredOnUtc });
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredOnUtc });
    }
}
