using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Domain.Orders;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.FoodOrders.Infrastructure.Auditing;
using HBA.FoodOrders.Infrastructure.Persistence.Outbox;
using HBA.FoodOrders.Infrastructure.Persistence.Inbox;
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
public sealed class MealOrderingDbContext : ModuleDbContext, IOutboxDbContext, IMealOrderUnitOfWork
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

    // ═════════════════════════════════════════════════════════════════════════
    // LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
    //
    // Le socle collecte les mutations, resout l'acteur et fixe l'instant unique de
    // la transaction ; il ne connait plus aucune table d'audit. Ces deux methodes
    // sont ce qu'il appelle, et elles repondent avec l'entite de `Auditing/`.
    // ═════════════════════════════════════════════════════════════════════════
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MealOrderingDbContext).Assembly);
        // Configuration du socle : elle vit dans un autre assembly, le balayage
        // ci-dessus ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
