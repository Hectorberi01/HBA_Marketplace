using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HBA.Food.Infrastructure.Persistence;

/// <summary>
/// Factory design-time pour les outils EF (`dotnet ef migrations add`).
///
/// Utilisée UNIQUEMENT par la ligne de commande : au démarrage réel, c'est
/// l'installer du module qui construit le DbContext. D'où le répartiteur d'events
/// inerte — générer une migration ne doit déclencher aucun effet de bord.
/// </summary>
public sealed class FoodDbContextFactory : IDesignTimeDbContextFactory<FoodDbContext>
{
    public FoodDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FOOD_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<FoodDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FoodDbContext.SchemaName))
            .Options;

        return new FoodDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
