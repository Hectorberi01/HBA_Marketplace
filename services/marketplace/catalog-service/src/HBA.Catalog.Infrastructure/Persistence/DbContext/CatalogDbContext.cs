using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Catalog.Application;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Brands;
using HBA.Catalog.Domain.Categories;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Domain.Reviews;

using HBA.Catalog.Infrastructure.Auditing;
using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Catalog.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Catalog.Infrastructure.Persistence;

/// <summary>DbContext du module Catalog.</summary>
public sealed class CatalogDbContext : ModuleDbContext, IOutboxDbContext, ICatalogUnitOfWork
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

    public const string SchemaName = "catalog";

    private readonly ICacheService _cache;

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Product> Products => Set<Product>();

    /// <summary>Les révisions, exposées pour la LECTURE seule.</summary>
    public DbSet<ProductRevision> ProductRevisions => Set<ProductRevision>();

    /// <summary>Le journal des décisions d'administration (§16, §20).</summary>
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Les demandes de marque des vendeurs (§10, §20).</summary>
    public DbSet<BrandRequest> BrandRequests => Set<BrandRequest>();

    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Le référentiel d'attributs (§10, §20).</summary>
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();

    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();

    /// <summary>Les offres — le PRIX, séparé de la fiche.</summary>
    public DbSet<ProductOffer> Offers => Set<ProductOffer>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§25).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

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
        // Applique d'abord les configs du module (Product…), puis la base (schéma
        // par défaut + outbox).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        // LES CONFIGS DU SOCLE VIVENT DANS UN AUTRE ASSEMBLAGE.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>INVALIDATION DU CACHE — POINT DE PASSAGE UNIQUE.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // AVANT le save : après, les entités ajoutées passent à « Unchanged » et
        // les supprimées sont détachées.
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
        var touchedProducts = new HashSet<Guid>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Product product:
                    touchedProducts.Add(product.Id.Value);
                    keys.Add(CatalogCacheKeys.ProductsBySeller(product.SellerId));
                    break;

                // Variantes et médias sont les ENFANTS de l'agrégat.
                case ProductVariant:
                case ProductMedia:
                    if (TryReadProductId(entry, out var childProductId))
                    {
                        touchedProducts.Add(childProductId);
                    }
                    break;

                // LA RÉVISION PORTE SON ProductId EN CLAIR, ELLE.
                case ProductRevision revision:
                    touchedProducts.Add(revision.ProductId.Value);
                    break;

                // ProductCondition et ProductDefect n'ont pas besoin de cas propre
                // : ils ne changent qu'à travers ProductRevision.Remplacer, qui
                // marque la révision comme modifiée dans la même unité de travail.

                // LA FICHE TECHNIQUE, ELLE, A BESOIN DU SIEN — ET LE RAISONNEMENT
                // CI-DESSUS EXPLIQUE POURQUOI.
                case ProductSpecificationGroup groupe:
                    if (TryResoudreProduitDeRevision(groupe.RevisionId, out var produitDuGroupe))
                    {
                        touchedProducts.Add(produitDuGroupe);
                    }
                    break;

                case ProductSpecification ligne:
                    if (TryResoudreProduitDeGroupe(ligne.GroupId, out var produitDeLaLigne))
                    {
                        touchedProducts.Add(produitDeLaLigne);
                    }
                    break;

                case Category category:
                    keys.Add(CatalogCacheKeys.Category(category.Id.Value));
                    keys.Add(CatalogCacheKeys.AllCategories);
                    break;

                case Brand brand:
                    keys.Add(CatalogCacheKeys.Brand(brand.Id.Value));
                    keys.Add(CatalogCacheKeys.AllBrands);
                    break;
            }
        }

        foreach (var productId in touchedProducts)
        {
            keys.Add(CatalogCacheKeys.Product(productId));

            // La liste de la boutique doit tomber elle aussi.
            var parent = ChangeTracker.Entries<Product>()
                .Select(e => e.Entity)
                .FirstOrDefault(p => p.Id.Value == productId);

            if (parent is not null)
            {
                keys.Add(CatalogCacheKeys.ProductsBySeller(parent.SellerId));
            }
        }

        return [.. keys];
    }

    /// <summary>
    /// Remonte d'une révision au produit qui la porte, en ne regardant que le
    /// ChangeTracker — aucune requête, on est en plein <c> SaveChanges</c>.
    /// </summary>
    private bool TryResoudreProduitDeRevision(Guid revisionId, out Guid productId)
    {
        var revision = ChangeTracker.Entries<ProductRevision>()
            .Select(e => e.Entity)
            .FirstOrDefault(r => r.Id == revisionId);

        productId = revision?.ProductId.Value ?? Guid.Empty;
        return productId != Guid.Empty;
    }

    /// <summary>Remonte d'un groupe de caractéristiques au produit, via sa révision.</summary>
    private bool TryResoudreProduitDeGroupe(Guid groupId, out Guid productId)
    {
        var groupe = ChangeTracker.Entries<ProductSpecificationGroup>()
            .Select(e => e.Entity)
            .FirstOrDefault(g => g.Id == groupId);

        if (groupe is null)
        {
            productId = Guid.Empty;
            return false;
        }

        return TryResoudreProduitDeRevision(groupe.RevisionId, out productId);
    }

    /// <summary>Lit la clé étrangère « ProductId » d'une variante ou d'un média.</summary>
    private static bool TryReadProductId(EntityEntry entry, out Guid productId)
    {
        productId = Guid.Empty;

        var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "ProductId");
        if (property is null)
        {
            return false;
        }

        // Sur une suppression, CurrentValue peut déjà être vidée : OriginalValue
        // garde alors la seule trace du parent.
        var raw = property.CurrentValue ?? property.OriginalValue;

        // La FK « ProductId » référence Product.Id, qui est un value object
        // fortement typé (`readonly record struct ProductId(Guid Value)`).
        var value = raw switch
        {
            ProductId typed => typed.Value,
            Guid guid => guid,
            _ => Guid.Empty,
        };

        if (value != Guid.Empty)
        {
            productId = value;
            return true;
        }

        return false;
    }
}
