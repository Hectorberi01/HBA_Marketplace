using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Domain.Staff;
using HBA.Food.Domain.Stations;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Food.Infrastructure.Auditing;
using HBA.Food.Infrastructure.Persistence.Outbox;
using HBA.Food.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Food.Infrastructure.Persistence;

/// <summary>LE SCHÉMA DU MODULE FOOD.</summary>
public sealed class FoodDbContext : ModuleDbContext, IOutboxDbContext, IFoodUnitOfWork
{
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
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

    public const string SchemaName = "food";

    public FoodDbContext(
        DbContextOptions<FoodDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();

    /// <summary>Les CARTES : « Menu du midi », « Carte du soir ».</summary>
    public DbSet<Menu> Menus => Set<Menu>();

    /// <summary>Les SECTIONS : « Entrées », « Plats », « Boissons ».</summary>
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    /// <summary>
    /// Le personnel (§8). Quatrième racine : un membre possède ses dérogations de
    /// permission, et rien d'autre.
    /// </summary>
    public DbSet<RestaurantStaff> Staff => Set<RestaurantStaff>();

    /// <summary>Les postes de préparation (§9) : GRILL, PIZZA, DRINKS.</summary>
    public DbSet<PreparationStation> PreparationStations => Set<PreparationStation>();

    /// <summary>La part OPÉRATIONNELLE des commandes (§10 à §13).</summary>
    public DbSet<FoodOrder> FoodOrders => Set<FoodOrder>();

    /// <summary>Traces de consommation Kafka (§19.5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    /// <summary>LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).</summary>
    protected override bool KeepsAuditTrail => true;

    // LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
    protected override void ConfigurerLeJournalDAudit(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new AuditConfiguration());

    protected override void AjouterUneEntreeDAudit(
        string typeDEntite,
        string identifiant,
        AuditOperation operation,
        Guid? acteur,
        string typeDActeur,
        string? correlation,
        DateTime instantUtc)
        => Set<AuditEntry>().Add(new AuditEntry
        {
            EntityType = typeDEntite,
            EntityId = identifiant,
            Operation = operation,
            ActorUserId = acteur,
            ActorType = typeDActeur,
            CorrelationId = correlation,
            OccurredOnUtc = instantUtc
        });


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FoodDbContext).Assembly);
        // Configuration du socle : autre assembly, le balayage ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
