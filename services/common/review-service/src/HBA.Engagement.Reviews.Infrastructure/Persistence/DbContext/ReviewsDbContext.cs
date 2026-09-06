using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Engagement.Reviews.Application;
using HBA.Engagement.Reviews.Application.Abstractions;
using HBA.Engagement.Reviews.Domain.Reviews;

namespace HBA.Engagement.Reviews.Infrastructure.Persistence;

/// <summary>DbContext du module Reviews (schéma « reviews »).</summary>
public sealed class ReviewsDbContext : ModuleDbContext, IReviewsUnitOfWork
{
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : le signalement, le rejet et la
    /// restauration d'un avis.
    ///
    /// La modération d'avis est le geste le plus contesté d'une place de marché : un
    /// vendeur dont l'avis négatif disparaît accuse la plateforme, et un avis rétabli
    /// fâche l'autre partie. Sans journal, aucune des deux plaintes n'est
    /// vérifiable.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `reviews.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Invalidation du cache des avis.
    ///
    /// Toute écriture sur un avis fait tomber DEUX clés du même produit : la liste
    /// d'avis, et la note agrégée. Ce sont deux vues d'une même vérité — publier un
    /// avis sans recalculer la note afficherait « 12 avis » sous une moyenne qui n'en
    /// compte que 11. Un utilisateur peut compter, et il comptera.
    ///
    /// La modération est concernée au même titre qu'une publication : retirer un avis
    /// change la moyenne autant qu'en ajouter un.
    /// </summary>
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
