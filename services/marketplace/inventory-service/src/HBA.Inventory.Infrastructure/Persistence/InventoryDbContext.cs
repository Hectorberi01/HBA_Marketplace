using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Domain.Locations;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Infrastructure.Persistence;

/// <summary>DbContext du module Inventory (schéma « inventory »).</summary>
public sealed class InventoryDbContext : ModuleDbContext, IInventoryUnitOfWork
{
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
