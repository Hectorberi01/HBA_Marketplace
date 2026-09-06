using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Domain.Promotions;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Promotions.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MODULE PROMOTION — SA PROPRE BASE (§9, §10.16 : <c>promotion_db</c>).
///
/// AUCUNE CLÉ ÉTRANGÈRE VERS UNE COMMANDE, UN PANIER OU UN UTILISATEUR.
///
/// `coupon_usages` porte `user_id`, `cart_id` et `order_id` en simples UUID. Ce
/// sont des références vers des agrégats qui vivent dans d'AUTRES bases — le §9
/// interdit à un service de lire celle d'un autre, donc une contrainte
/// référentielle serait impossible à honorer de toute façon.
///
/// La conséquence est assumée : rien n'empêche d'enregistrer un usage sur une
/// commande qui n'existe pas. Ce que l'on gagne en échange, c'est qu'une campagne
/// promotionnelle ne bloque jamais la suppression d'une commande, et que ce
/// service reste redéployable seul.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PromotionsDbContext : ModuleDbContext, IPromotionsUnitOfWork
{
    public const string SchemaName = "promotions";

    public PromotionsDbContext(
        DbContextOptions<PromotionsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<PromotionRule> PromotionRules => Set<PromotionRule>();

    public DbSet<Coupon> Coupons => Set<Coupon>();

    /// <summary>Table <c>coupon_usages</c> du §10.16 — retenues ET usages engagés.</summary>
    public DbSet<CouponReservation> CouponUsages => Set<CouponReservation>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).
    ///
    /// INDISPENSABLES ICI, PLUS QU'AILLEURS.
    ///
    /// Ce service consomme `marketplace.order.cancelled` et
    /// `food.order.cancelled`, dont le traitement RE-CRÉDITE un budget. Un rejeu
    /// non filtré ne produit pas une ligne en double visible : il gonfle une
    /// enveloppe, et la campagne ne s'épuise jamais. Le domaine se défend déjà
    /// seul — `RevokeForCancelledOrder` ne trouve plus d'usage engagé au second
    /// passage — mais deux gardes valent mieux qu'une quand la panne est
    /// silencieuse.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromotionsDbContext).Assembly);

        // Les configurations du socle vivent dans un AUTRE assembly : le balayage
        // ci-dessus ne les trouve pas. Les oublier ne casse rien à la compilation —
        // les tables manquent simplement, et l'erreur ne surgit qu'au premier
        // message consommé, en production.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
