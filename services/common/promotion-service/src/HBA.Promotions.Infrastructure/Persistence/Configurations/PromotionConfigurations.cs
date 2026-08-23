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
        //
        // Le §10.16 décrit `scope FOOD|MARKETPLACE|GLOBAL` : ce sont des valeurs
        // lisibles, pas des indices. Stocker l'entier rendrait toute requête
        // d'exploitation illisible — « scope = 2 » n'aide personne à minuit — et
        // surtout, réordonner l'énumération réécrirait silencieusement le sens de
        // toutes les lignes déjà en base.
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // ═════════════════════════════════════════════════════════════════════
        // BIGINT (§2, D39). Le franc CFA n'a pas de sous-unité ; un `numeric`
        // ici rouvrirait la porte aux arrondis que ce choix ferme.
        //
        // C'EST L'UNE DES DEUX SEULES ÎLES EN ENTIER DU DÉPÔT — l'autre est
        // `delivery_pricing`. Partout ailleurs l'argent est en `numeric(18,2)`.
        // La frontière est tenue par `PromotionPricingModuleApi.EnUnitesEntieres`
        // (decimal → long, arrondi au plus proche) et par la répartition au
        // `Math.Floor` dans l'autre sens. Les deux sont commentées sur place.
        //
        // CE QUE CE CHOIX SUPPOSE, ET QUE RIEN NE VÉRIFIE : que `Currency`
        // vaut XOF. Une campagne en euros stockerait « 10 » pour dix euros, et
        // une remise de 10,50 € deviendrait 10 ou 11 selon l'arrondi. Le champ
        // est un texte libre de trois lettres ; aucune contrainte ne le borne.
        // ═════════════════════════════════════════════════════════════════════
        builder.Property(p => p.Value).IsRequired();
        builder.Property(p => p.Budget);
        builder.Property(p => p.BudgetConsumed).IsRequired().HasDefaultValue(0L);

        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();

        // ═════════════════════════════════════════════════════════════════════
        // UNE PART EN POINTS DE BASE, PAS UN BOOLÉEN NI UNE ÉNUMÉRATION.
        //
        // D28 exige que le champ « permette d'exprimer plus tard une remise
        // COFINANCÉE sans migration supplémentaire ». Un `funded_by` en texte
        // (« PLATFORM » / « SELLER ») aurait demandé une colonne de plus le jour
        // du premier partage — donc une migration, un déploiement coordonné, et
        // une période où deux colonnes disent la même chose à moitié.
        //
        // `integer` et non `smallint` : la marge coûte deux octets par campagne et
        // évite une seconde migration si la précision doit changer.
        //
        // Le DÉFAUT RESTE POSÉ SUR LA COLONNE après la migration, pour qu'une
        // insertion qui ne passerait pas par EF — un jeu de données, une reprise
        // SQL — soit correcte elle aussi.
        // ═════════════════════════════════════════════════════════════════════
        builder.Property(p => p.SellerFundedShareBps).IsRequired().HasDefaultValue(0);

        builder.Property(p => p.OwnerSellerId);

        builder.Property(p => p.StartsAtUtc).IsRequired();
        builder.Property(p => p.EndsAtUtc).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();

        // LES RÈGLES SONT CHARGÉES AVEC LA CAMPAGNE, PAS À LA DEMANDE.
        //
        // Le chargement paresseux est désactivé dans ce dépôt. Une campagne dont
        // la collection `Rules` n'est pas incluse présente une liste VIDE, et
        // `EnsureApplicable` ne trouve alors rien à refuser : la remise part sur
        // des paniers qui n'y avaient pas droit, sans qu'aucune erreur ne soit
        // levée nulle part. C'est le dépôt qui fait l'inclusion — ici on se
        // contente de déclarer la relation et sa cascade.
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
        //
        // `GET /api/v1/merchant/promotions` filtre sur le propriétaire depuis D28 :
        // sans index, chaque ouverture du tableau de bord d'un vendeur balaie la
        // table entière. L'index est PARTIEL — les campagnes de la plateforme
        // portent `NULL` et ne se cherchent jamais par ce chemin, les indexer
        // doublerait sa taille pour aucune requête.
        builder.HasIndex(p => p.OwnerSellerId)
            .HasFilter("\"OwnerSellerId\" IS NOT NULL")
            .HasDatabaseName("ix_promotions_owner_seller");

        // ═════════════════════════════════════════════════════════════════════
        // JETON DE CONCURRENCE — ET IL LÈVE UNE CONTRAINTE DE DÉPLOIEMENT ÉCRITE.
        //
        // `Promotion.BudgetConsumed` est un compteur d'argent : chaque réservation
        // de remise l'incrémente. Sans jeton, deux réservations simultanées lisent
        // le même solde, ajoutent chacune leur part, et la seconde écrase la
        // première — la campagne dépasse son budget sans que rien ne le dise. Sur
        // une remise financée par la plateforme, c'est de l'argent perdu ; sur une
        // remise financée par le vendeur, c'est de l'argent qu'on lui prend.
        //
        // `ExpireCouponHoldsWorker` LE DISAIT DÉJÀ, ET S'EN SERVAIT COMME D'UNE
        // LIMITE DE DÉPLOIEMENT : « les deux écritures concurrentes sur
        // `Promotion.BudgetConsumed` ne sont pas protégées par un jeton de
        // concurrence dans ce module. Avant de mettre promotion-service à l'échelle
        // horizontale, il faut soit le verrou de ligne, soit un jeton de version
        // sur la campagne. C'est une contrainte de déploiement, pas une opinion. »
        //
        // C'est ce jeton-là. Son encadré a été mis à jour dans le même geste :
        // laisser un texte qui annonce une limite levée est exactement le défaut
        // que ce chantier passe son temps à retirer.
        //
        // `BudgetConsumed` est une colonne de CETTE table : le jeton n'est pas
        // inerte. Le perdant reçoit 409 et la réservation est rejouée sur l'état à
        // jour — donc refusée si le budget est réellement épuisé, ce qui est le
        // comportement voulu.
        //
        // AUCUNE COLONNE N'EST CRÉÉE : `xmin` est une colonne système.
        // ═════════════════════════════════════════════════════════════════════
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
        //
        // Postgres valide la syntaxe à l'écriture : une règle syntaxiquement
        // illisible est refusée par la base au lieu d'attendre le premier
        // checkout pour se manifester. `PromotionRule.Evaluate` sait déjà se
        // défendre — il rend `promotions.rule.malformed` — mais échouer à
        // l'écriture vaut mieux qu'échouer à la lecture, six mois plus tard,
        // devant un client.
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
        //
        // Deux coupons portant le même code rendraient `GetByCodeAsync` ambigu :
        // la recherche ramènerait le premier venu, donc potentiellement la
        // campagne la plus généreuse — et le choix changerait au gré du plan
        // d'exécution. La base est le seul endroit où cette unicité tient face à
        // deux créations simultanées.
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

/// <summary>
/// Mapping de la table <c>coupon_usages</c> (§10.16).
///
/// UNE SEULE TABLE POUR LES RETENUES ET LES USAGES ENGAGÉS.
///
/// Le cahier ne décrit que `coupon_usages`. Séparer les deux aurait obligé à
/// DÉPLACER une ligne au moment de l'engagement — donc à perdre l'instant de la
/// retenue, et à rendre le passage non atomique. Ici c'est une colonne `status`
/// qui change, et l'historique reste entier.
/// </summary>
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
        //
        // C'est la contrainte qui rend le « double-clic sur appliquer » inoffensif
        // même quand deux requêtes arrivent en parallèle : le domaine relit ses
        // retenues avant d'en créer une, mais deux transactions concurrentes lisent
        // toutes les deux « aucune » et créeraient deux lignes — donc deux
        // consommations de budget pour un panier.
        //
        // L'index est PARTIEL : seules les retenues encore tenues comptent. Sans le
        // filtre, un client ne pourrait jamais réutiliser un coupon sur un panier
        // dont la retenue précédente a expiré.
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
