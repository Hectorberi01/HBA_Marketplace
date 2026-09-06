using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Infrastructure.Persistence;

/// <summary>DbContext du module Ordering (schéma « ordering »).</summary>
public sealed class OrderingDbContext : ModuleDbContext, IOrderingUnitOfWork
{
    public const string SchemaName = "ordering";

    public OrderingDbContext(
        DbContextOptions<OrderingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// Les parts vendeur (ISSUE-027).
    ///
    /// UN DbSet PROPRE, ET NON UNE NAVIGATION SOUS `Orders`.
    ///
    /// `SellerOrder` est un AGRÉGAT, avec son verrou optimiste et ses
    /// transitions. Le charger sous la commande ferait salir `orders` à chaque
    /// geste d'un vendeur, donc mettrait deux vendeurs d'une même commande en
    /// concurrence sur la MÊME ligne parente : deux confirmations simultanées se
    /// renverraient un 409 l'une l'autre, pour deux gestes indépendants.
    /// </summary>
    public DbSet<SellerOrder> SellerOrders => Set<SellerOrder>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5).
    ///
    /// Elles n'appartiennent à aucun agrégat : c'est le dispatcher qui les pose,
    /// dans la MÊME transaction que l'effet métier du gestionnaire. Le DbSet
    /// existe pour que la table se voie depuis le contexte comme n'importe quelle
    /// autre — l'inbox y écrit par <c>Set&lt;ConsumerInboxEntry&gt;()</c>.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : le remboursement décidé après arbitrage,
    /// la reprise d'une commande suspendue, et l'annulation par l'exploitation.
    ///
    /// SECOND CONTEXTE LE PLUS ÉCRIT DU DÉPÔT — même arbitrage que pour les
    /// courses. Journaliser aussi le passage de commande d'un acheteur n'est pas du
    /// bruit : c'est la même question, « qui a fait quoi », avec `ActorType = USER`.
    ///
    /// CE SCHÉMA ÉTAIT LUI AUSSI ANNONCÉ COMME JOURNALISÉ par `AuditQueries` et
    /// `SellersDbContext`, sans surcharge ni table.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `ordering.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
        // Configuration du socle : autre assembly, le balayage ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
