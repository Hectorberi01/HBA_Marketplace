using HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;
using HBA.Delivery.Pricing.Domain.Entities;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Delivery.Pricing.Infrastructure.Persistence;

public sealed class DeliveryPricingDbContext : ModuleDbContext
{
    public const string SchemaName = "delivery_pricing";

    public DeliveryPricingDbContext(
        DbContextOptions<DeliveryPricingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<DeliveryQuote> DeliveryQuotes => Set<DeliveryQuote>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : la création, l'édition, l'activation et la
    /// désactivation d'une RÈGLE DE TARIFICATION.
    ///
    /// Une grille éditée change le prix de toutes les courses suivantes. C'est le
    /// geste au plus fort effet de levier de la plateforme, et le plus discret : rien
    /// dans une course ne dit quelle version de la grille l'a chiffrée.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `delivery_pricing.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryQuote>(entity =>
        {
            entity.ToTable("delivery_quotes", SchemaName);
            entity.HasKey(quote => quote.Id);
            entity.OwnsOne(quote => quote.Pickup);
            entity.OwnsOne(quote => quote.Dropoff);
            entity.OwnsOne(quote => quote.Components);
            entity.Property(quote => quote.Status).HasMaxLength(30);
            entity.Property(quote => quote.Currency).HasMaxLength(3);
            entity.Property(quote => quote.ServiceLevel).HasMaxLength(30);
            entity.Property(quote => quote.PricingVersion).HasMaxLength(40);

            // Voir `SourcesEstimation` : ce qui a produit la distance et la durée
            // qui ont chiffré ce devis. Colonne NOT NULL avec défaut vide — une
            // chaîne vide se lit « on ne sait pas », ce qui est exact pour toute
            // ligne écrite avant la migration.
            entity.Property(quote => quote.SourceEstimation).HasMaxLength(40);
            entity.Property(quote => quote.FacteurCorrectionApplique).HasPrecision(4, 2);
        });

        modelBuilder.Entity<PricingRule>(entity =>
        {
            entity.ToTable("pricing_rules", SchemaName);
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Name).HasMaxLength(160);
            entity.Property(rule => rule.Scope).HasMaxLength(40);
            entity.Property(rule => rule.ServiceLevel).HasMaxLength(30);
            entity.Property(rule => rule.VehicleType).HasMaxLength(40);
            entity.Property(rule => rule.Status).HasMaxLength(30);
        });

        modelBuilder.Entity<DeliveryZone>(entity =>
        {
            entity.ToTable("delivery_zones", SchemaName);
            entity.HasKey(zone => zone.Id);
            entity.Property(zone => zone.Name).HasMaxLength(160);
            entity.Property(zone => zone.GeometryRef).HasMaxLength(240);
        });

        base.OnModelCreating(modelBuilder);
    }
}
