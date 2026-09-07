using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Engagement.Reviews.Application;
using HBA.Engagement.Reviews.Application.Abstractions;
using HBA.Engagement.Reviews.Domain.Reviews;

using HBA.Engagement.Reviews.Infrastructure.Auditing;
using HBA.Engagement.Reviews.Infrastructure.Persistence.Outbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Engagement.Reviews.Infrastructure.Persistence;

/// <summary>DbContext du module Reviews (schéma « reviews »).</summary>
public sealed class ReviewsDbContext : ModuleDbContext, IOutboxDbContext, IReviewsUnitOfWork
{
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        // PAS D'INBOX : ce service ne consomme aucun evenement d'integration.
        // La ligne `ApplyConfiguration(new ConsumerInboxConfiguration())` mappait
        // `consumer_inbox` dans le modele alors qu'aucune migration ne cree cette
        // table — et `InboxCleanupService` l'interrogeait a chaque tick.
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

    public const string SchemaName = "reviews";

    private readonly ICacheService _cache;

    public ReviewsDbContext(
        DbContextOptions<ReviewsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Review> Reviews => Set<Review>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Invalidation du cache des avis.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var keysToEvict = CollectCacheKeysToEvict();

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (keysToEvict.Count > 0)
        {
            await _cache.RemoveManyAsync(keysToEvict, cancellationToken);
        }

        return affected;
    }

    private List<string> CollectCacheKeysToEvict()
    {
        var keys = new HashSet<string>();

        foreach (var entry in ChangeTracker.Entries<Review>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            keys.Add(ReviewsCacheKeys.Rating(entry.Entity.ProductId));
            keys.Add(ReviewsCacheKeys.ByProduct(entry.Entity.ProductId));
        }

        return [.. keys];
    }
}
