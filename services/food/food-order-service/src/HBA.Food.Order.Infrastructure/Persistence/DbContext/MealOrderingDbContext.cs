using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Domain.Orders;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.FoodOrders.Infrastructure.Persistence;

/// <summary>
/// DbContext des commandes de repas (schéma « food_ordering »).
///
/// SCHÉMA PROPRE, ET PAS UNE TABLE DE PLUS DANS « ordering ».
///
/// Deux services qui écrivent dans le même schéma partagent leur table
/// `__ef_migrations_history` : chacun croirait devoir rejouer les migrations de
/// l'autre, et `MigrateOnStartup` échouerait au démarrage du second.
/// </summary>
public sealed class MealOrderingDbContext : ModuleDbContext, IMealOrderUnitOfWork
{
    public const string SchemaName = "food_ordering";

    public MealOrderingDbContext(
        DbContextOptions<MealOrderingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<MealOrder> Orders => Set<MealOrder>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// LE JOURNAL D'AUDIT EST ACTIF, COMME CHEZ SON JUMEAU MARKETPLACE.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `audit_entries` : une
    /// entité présente dans le modèle sans table correspondante fait échouer tout
    /// démarrage à froid, et le contrôle `migrations` le refuse.
    ///
    /// Une commande est un fait comptable — qui l'a annulée, quand, après quel
    /// arbitrage. C'est la première chose qu'on cherche devant une réclamation.
    /// </summary>
    protected override bool KeepsAuditTrail => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MealOrderingDbContext).Assembly);
        // Configuration du socle : elle vit dans un autre assembly, le balayage
        // ci-dessus ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
