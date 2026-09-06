using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HBA.Promotions.Infrastructure.Persistence;

/// <summary>
/// Factory design-time pour les outils EF (`dotnet ef migrations add`).
///
/// ELLE EST AUTONOME, ET C'EST CE QUI REND `make migrations` UTILISABLE.
///
/// Chaîne de connexion par défaut, répartiteur d'événements inerte : générer une
/// migration ne lit que le MODÈLE, ne contacte aucune base et ne doit déclencher
/// aucun effet de bord. Sans elle, il faudrait un postgres démarré — donc docker —
/// pour produire un fichier qui ne dépend d'aucun des deux.
/// </summary>
public sealed class PromotionsDbContextFactory : IDesignTimeDbContextFactory<PromotionsDbContext>
{
    public PromotionsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PROMOTIONS_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PromotionsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PromotionsDbContext.SchemaName))
            .Options;

        return new PromotionsDbContext(
            options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(
            IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
