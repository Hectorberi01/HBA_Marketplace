using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HBA.FoodCarts.Infrastructure.Persistence;

/// <summary>Factory design-time pour les outils EF.</summary>
public sealed class FoodCartDbContextFactory : IDesignTimeDbContextFactory<FoodCartDbContext>
{
    public FoodCartDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FOOD_CART_DB")
            ?? "Host=localhost;Port=5432;Database=food;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<FoodCartDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FoodCartDbContext.SchemaName))
            .Options;

        return new FoodCartDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(
            IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
