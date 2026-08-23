using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Partners;
using HBA.Deliveries.Domain.Webhooks;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

/// <summary>DbContext du module Deliveries (schéma « deliveries »).</summary>
public sealed class DeliveriesDbContext : ModuleDbContext, IDeliveryUnitOfWork
{
    public const string SchemaName = "deliveries";

    public DeliveriesDbContext(
        DbContextOptions<DeliveriesDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    /// <summary>
    /// Persiste, et rend <c>false</c> au lieu de lever sur un conflit de
    /// concurrence. Voir l'encadré d'<see cref="IDeliveryUnitOfWork"/> : c'est le
    /// seul chemin de ce module qui peut se permettre de perdre son écriture.
    /// </summary>
    /// <remarks>
    /// SEUL `DbUpdateConcurrencyException` EST AVALÉ. `DbUpdateException` — une
    /// violation de contrainte — et tout le reste remontent. Élargir cette capture
    /// transformerait un point d'entrée volontairement étroit en `SaveChanges`
    /// silencieux, et c'est exactement ce qu'il ne doit pas devenir.
    ///
    /// AUCUN REJEU. Recharger et réécrire dans le même scope re-dispatcherait
    /// les événements de domaine et dupliquerait l'outbox — voir l'encadré
    /// d'`UsePostgresRowVersion`. L'appelant décide quoi faire du <c>false</c> ;
    /// aujourd'hui, il l'ignore et attend le prochain battement.
    /// </remarks>
    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public DbSet<Domain.Deliveries.Delivery> Deliveries => Set<Domain.Deliveries.Delivery>();

    public DbSet<Driver> Drivers => Set<Driver>();

    // ── Partenaires (API publique) ──────────────────────────────────────────
    public DbSet<Partner> Partners => Set<Partner>();

    // ── Webhooks sortants ───────────────────────────────────────────────────
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5).
    ///
    /// Ce n'est pas une table métier : personne ne la lit hors du répartiteur.
    /// Elle est ici parce qu'une trace écrite dans une AUTRE transaction que
    /// l'effet qu'elle protège ne protège rien — c'est le `SaveChangesAsync` de
    /// ce contexte qui rend les deux atomiques.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : l'annulation d'une course, et le retrait
    /// d'une affectation à un livreur.
    ///
    /// C'EST L'UN DES DEUX CONTEXTES LES PLUS ÉCRITS DU DÉPÔT, ET LE COÛT EST
    /// ASSUMÉ.
    ///
    /// Chaque transition de course écrira désormais une ligne de journal
    /// supplémentaire, dans la même transaction. `KeepsAuditTrail` est une propriété
    /// du MODÈLE : il n'existe pas de réglage « journaliser seulement les gestes
    /// d'exploitation ». Le choix est donc entre tout journaliser et ne rien
    /// journaliser — et une course annulée sans trace est précisément le litige
    /// qu'on ne sait pas arbitrer. Voir `AuditPurger` pour la rétention.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `deliveries.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveriesDbContext).Assembly);
        // Configuration du socle : autre assembly, le balayage ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
