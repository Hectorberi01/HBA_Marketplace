using HBA.FoodCarts.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using CartAggregate = HBA.FoodCarts.Domain.Carts.FoodCart;

using HBA.FoodCarts.Infrastructure.Persistence.Outbox;
using HBA.FoodCarts.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.FoodCarts.Infrastructure.Persistence;

/// <summary>DbContext du panier de restauration (schéma « food_cart »).</summary>
public sealed class FoodCartDbContext : ModuleDbContext, IOutboxDbContext, IFoodCartUnitOfWork
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

    public const string SchemaName = "food_cart";

    public FoodCartDbContext(
        DbContextOptions<FoodCartDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<CartAggregate> Carts => Set<CartAggregate>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FoodCartDbContext).Assembly);
        // Configuration du socle : elle vit dans un autre assembly, le balayage
        // ci-dessus ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
