using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Domain.Locations;
using HBA.Inventory.Domain.Stock;

using HBA.Inventory.Infrastructure.Persistence.Outbox;
using HBA.Inventory.Infrastructure.Persistence.Inbox;
namespace HBA.Inventory.Infrastructure.Persistence;

/// <summary>DbContext du module Inventory (schéma « inventory »).</summary>
public sealed class InventoryDbContext : ModuleDbContext, IOutboxDbContext, IInventoryUnitOfWork
{
    // ═════════════════════════════════════════════════════════════════════════
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    //
    // Le socle draine la file d'evenements et exclut ces deux tables du journal
    // d'audit ; il ne connait plus ni l'une ni l'autre. Ces trois membres sont ce
    // qu'il appelle, et ils repondent avec les entites de `Persistence/`.
    // ═════════════════════════════════════════════════════════════════════════
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
    }

    protected override void AjouterAuOutbox(
        string type, string contenu, DateTime survenuLeUtc, string? traceParent, string? correlation)
        => OutboxMessages.Add(new OutboxMessage
        {
            Type = type,
            Content = contenu,
            OccurredOnUtc = survenuLeUtc,
            TraceParent = traceParent,
            CorrelationId = correlation
        });

    public const string SchemaName = "inventory";

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<FulfillmentLocation> FulfillmentLocations => Set<FulfillmentLocation>();

    /// <summary>
    /// Le journal des mouvements de stock (lot 7.3, ISSUE-044).
    ///
    /// EXPOSÉ EN `DbSet` BIEN QUE SEUL `StockMovementRepository` l écrive : sans
    /// lui, la table serait mappée par la seule configuration et n apparaîtrait dans
    /// aucune signature. Le prochain lecteur de ce fichier doit voir ce que ce
    /// schéma contient.
    /// </summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
