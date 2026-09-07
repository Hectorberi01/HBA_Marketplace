using HBA.Promotions.Domain.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Promotions.Infrastructure.Persistence.Configurations;

/// <summary>Mapping de la table <c>promotions</c> (§10.16).</summary>
public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();

        // LES ÉNUMÉRATIONS SONT STOCKÉES EN TEXTE, PAS EN ENTIER.
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // BIGINT (§2, D39). Le franc CFA n'a pas de sous-unité ; un `numeric` ici
        // rouvrirait la porte aux arrondis que ce choix ferme.
        builder.Property(p => p.Value).IsRequired();
        builder.Property(p => p.Budget);
        builder.Property(p => p.BudgetConsumed).IsRequired().HasDefaultValue(0L);

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();

        // UNE PART EN POINTS DE BASE, PAS UN BOOLÉEN NI UNE ÉNUMÉRATION.
        builder.Property(p => p.SellerFundedShareBps).IsRequired().HasDefaultValue(0);

        builder.Property(p => p.OwnerSellerId);

        builder.Property(p => p.StartsAtUtc).IsRequired();
        builder.Property(p => p.EndsAtUtc).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();

        // LES RÈGLES SONT CHARGÉES AVEC LA CAMPAGNE, PAS À LA DEMANDE.
        builder.HasMany(p => p.Rules)
            .WithOne()
            .HasForeignKey(r => r.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Rules).UsePropertyAccessMode(PropertyAccessMode.Field);

        // La requête « campagnes actives de cet univers », exécutée à chaque
        // évaluation de panier.
        builder.HasIndex(p => new { p.Scope, p.Status }).HasDatabaseName("ix_promotions_scope_status");

        // Purge et pilotage : les campagnes terminées se retrouvent par leur fin.
        builder.HasIndex(p => p.EndsAtUtc).HasDatabaseName("ix_promotions_ends_at");

        // « MES CAMPAGNES » EST DÉSORMAIS UNE REQUÊTE DE PRODUCTION.
        builder.HasIndex(p => p.OwnerSellerId)
            .HasFilter("\"OwnerSellerId\" IS NOT NULL")
            .HasDatabaseName("ix_promotions_owner_seller");

        // JETON DE CONCURRENCE — ET IL LÈVE UNE CONTRAINTE DE DÉPLOIEMENT ÉCRITE.
        builder.UsePostgresRowVersion();

        // Lecture dérivée de `SellerFundedShareBps` — voir `Promotion.Funder`.
        builder.Ignore(p => p.Funder);
        builder.Ignore(p => p.BudgetRemaining);
        builder.Ignore(p => p.DomainEvents);
    }
}

/// <summary>Mapping de la table <c>promotion_rules</c> (§10.16).</summary>
public sealed class PromotionRuleConfiguration : IEntityTypeConfiguration<PromotionRule>
{
    public void Configure(EntityTypeBuilder<PromotionRule> builder)
    {
        builder.ToTable("promotion_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PromotionId).IsRequired();
        builder.Property(r => r.RuleType).HasMaxLength(60).IsRequired();

        // `jsonb` ET NON `text`.
        builder.Property(r => r.RuleJson).HasColumnType("jsonb").IsRequired();

        builder.Property(r => r.CreatedAtUtc).IsRequired();

        builder.HasIndex(r => r.PromotionId).HasDatabaseName("ix_promotion_rules_promotion");
    }
}

/// <summary>Mapping de la table <c>coupons</c> (§10.16).</summary>
public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons");
        builder.HorodateLesModifications();

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.PromotionId).IsRequired();
        builder.Property(c => c.Code).HasMaxLength(60).IsRequired();
        builder.Property(c => c.MaxUses);
        builder.Property(c => c.PerUserLimit);
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // `code UNIQUE` DU §10.16, ET C'EST UNE RÈGLE MÉTIER, PAS UN CONFORT.
        builder.HasIndex(c => c.Code).IsUnique().HasDatabaseName("ux_coupons_code");

        builder.HasMany(c => c.Reservations)
            .WithOne()
            .HasForeignKey(r => r.CouponId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => c.PromotionId).HasDatabaseName("ix_coupons_promotion");

        builder.Ignore(c => c.DomainEvents);
    }
}

/// <summary>Mapping de la table <c>coupon_usages</c> (§10.16).</summary>
public sealed class CouponReservationConfiguration : IEntityTypeConfiguration<CouponReservation>
{
    public void Configure(EntityTypeBuilder<CouponReservation> builder)
    {
        builder.ToTable("coupon_usages");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CouponId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.CartId).IsRequired();
        builder.Property(r => r.OrderId);
        builder.Property(r => r.DiscountAmount).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ExpiresAtUtc).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.CommittedAtUtc);

        // UNE SEULE RETENUE VIVANTE PAR PANIER ET PAR COUPON.
        builder.HasIndex(r => new { r.CouponId, r.CartId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Held'")
            .HasDatabaseName("ux_coupon_usages_live_hold");

        // Le plafond par compte, interrogé à chaque réservation.
        builder.HasIndex(r => new { r.CouponId, r.UserId })
            .HasDatabaseName("ix_coupon_usages_coupon_user");

        // L'entrée du consommateur d'annulation : il ne connaît que la commande.
        builder.HasIndex(r => r.OrderId)
            .HasFilter("\"OrderId\" IS NOT NULL")
            .HasDatabaseName("ix_coupon_usages_order");

        // Le ménage des retenues expirées.
        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasFilter("\"Status\" = 'Held'")
            .HasDatabaseName("ix_coupon_usages_expiring");
    }
}
