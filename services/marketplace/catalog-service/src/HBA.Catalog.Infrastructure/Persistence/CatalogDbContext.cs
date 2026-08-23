using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Catalog.Application;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Brands;
using HBA.Catalog.Domain.Categories;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Catalog. Vit dans le schéma « catalog » : pas de JOIN ni
/// de foreign key vers un autre schéma. Hérite de ModuleDbContext pour l'Unit of
/// Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class CatalogDbContext : ModuleDbContext, ICatalogUnitOfWork
{
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

    /// <summary>
    /// Les révisions, exposées pour la LECTURE seule.
    ///
    /// ON N'ÉCRIT JAMAIS ICI. Une révision naît et avance par l'agrégat Product,
    /// qui seul sait quand une modification est critique (§6) et quelle révision
    /// reste servie au public. Écrire directement dans ce DbSet contournerait les
    /// deux règles d'un coup, et rien ne le signalerait.
    ///
    /// Il existe parce que les lectures publiques partent de la RÉVISION PUBLIÉE —
    /// recherche, fiche par slug, arbre de catégories — et charger l'agrégat entier
    /// pour projeter quatre champs coûterait ses variantes, ses médias et toutes
    /// ses versions antérieures.
    /// </summary>
    public DbSet<ProductRevision> ProductRevisions => Set<ProductRevision>();

    /// <summary>
    /// Le journal des décisions d'administration (§16, §20).
    ///
    /// Agrégat à part, dans le même contexte — même raison que les offres : un
    /// contexte EF délimite une BASE, pas un agrégat.
    /// </summary>
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Les demandes de marque des vendeurs (§10, §20).</summary>
    public DbSet<BrandRequest> BrandRequests => Set<BrandRequest>();

    public DbSet<Category> Categories => Set<Category>();

    /// <summary>
    /// Le référentiel d'attributs (§10, §20).
    ///
    /// Les définitions sont PARTAGÉES entre catégories ; `CategoryAttributes` porte
    /// ce qui dépend de la catégorie — obligatoire, formant variante, position.
    /// Aucune navigation ne les relie : voir `CategoryAttributeRepository`.
    /// </summary>
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();

    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();

    /// <summary>
    /// Les offres — le PRIX, séparé de la fiche.
    ///
    /// AGRÉGAT RACINE À PART, DANS LE MÊME CONTEXTE. Ce n'est pas une
    /// contradiction : un contexte EF délimite une BASE, pas un agrégat. Les
    /// offres vivent dans `hba_catalog` parce que catalog-service les sert
    /// (`catalog.proto` déclare les quatre RPC d'offre sur `CatalogApi`), et
    /// elles gardent leur propre cycle de vie, leur propre dépôt et leur propre
    /// table.
    ///
    /// Ce qu'il ne faut PAS en conclure : aucune propriété de navigation ne relie
    /// `Product` et `ProductOffer`. L'offre référence le produit par identifiant,
    /// pour que changer un prix ne charge pas la fiche entière avec ses variantes,
    /// ses médias et les offres de tous les autres vendeurs.
    /// </summary>
    public DbSet<ProductOffer> Offers => Set<ProductOffer>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5) et requêtes idempotentes (§25).
    ///
    /// DANS LE SCHÉMA DU SERVICE, PAS DANS UNE BASE COMMUNE.
    ///
    /// Le §9 interdit qu'un service lise la base d'un autre, et une inbox partagée
    /// serait exactement cela — avec en prime un point de panne unique posé sur le
    /// chemin de TOUTES les consommations de la plateforme.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : l'approbation et le refus d'une fiche
    /// produit, la modération des marques — et, côté vendeur, toute modification de
    /// PRIX d'une offre.
    ///
    /// CE SCHÉMA ÉTAIT DÉJÀ ANNONCÉ COMME JOURNALISÉ. `AuditQueries` et
    /// `SellersDbContext` affirmaient tous deux que catalog tenait un journal, et
    /// promettaient « qui a modifié ce prix » comme une simple route à écrire. Ni la
    /// surcharge ni la table n'ont jamais existé. Cette migration rend le commentaire
    /// vrai.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `catalog.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Applique d'abord les configs du module (Product…), puis la base
        // (schéma par défaut + outbox).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);

        // LES CONFIGS DU SOCLE VIVENT DANS UN AUTRE ASSEMBLAGE.
        //
        // Le balayage ci-dessus ne parcourt que celui de `CatalogDbContext` : il ne
        // les trouve pas. Les oublier ne casse RIEN à la compilation — les deux
        // tables manquent simplement, et l'erreur ne surgit qu'au premier message
        // consommé ou à la première requête portant une `Idempotency-Key`,
        // c'est-à-dire en production.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// ─────────────────────────────────────────────────────────────────────────
    /// INVALIDATION DU CACHE — POINT DE PASSAGE UNIQUE.
    ///
    /// L'autre solution aurait été d'invalider dans chacun des SEIZE handlers de
    /// commande du module. Elle a été écartée : il aurait suffi qu'UN handler
    /// oublie son invalidation — ou qu'un dix-septième soit écrit demain sans y
    /// penser — pour que le catalogue serve indéfiniment une donnée périmée. Ce
    /// bug-là est invisible en développement (le cache y est vide) et permanent en
    /// production, sans erreur ni trace dans les journaux.
    ///
    /// Le ChangeTracker, lui, SAIT exactement ce qui a changé, et toute écriture
    /// passe forcément par ici. Rien ne peut être oublié.
    ///
    /// L'ORDRE COMPTE : on évince APRÈS le commit.
    ///
    /// Évincer avant, ce serait offrir à une lecture concurrente l'occasion de
    /// recharger l'ANCIENNE valeur en cache juste avant que la nouvelle ne soit
    /// validée — le cache resterait alors faux jusqu'à l'expiration de son TTL.
    ///
    /// Il subsiste une fenêtre de quelques millisecondes entre le commit et
    /// l'éviction, pendant laquelle une lecture peut réécrire l'ancienne valeur.
    /// C'est le compromis assumé du cache-aside, et les TTL courts le bornent. La
    /// fermer exigerait un verrou distribué, dont le coût dépasserait de loin le
    /// préjudice : voir un nom de produit périmé pendant cinq minutes.
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // AVANT le save : après, les entités ajoutées passent à « Unchanged » et les
        // supprimées sont détachées. Le ChangeTracker aurait tout oublié.
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

                // Variantes et médias sont les ENFANTS de l'agrégat. Ajouter une
                // photo modifie l'enfant, pas le Product : un invalidateur qui ne
                // regarderait que les Product laisserait la fiche en cache avec son
                // ancienne image. C'est exactement le genre d'oubli que ce point de
                // passage unique existe pour rendre impossible.
                case ProductVariant:
                case ProductMedia:
                    if (TryReadProductId(entry, out var childProductId))
                    {
                        touchedProducts.Add(childProductId);
                    }
                    break;

                // LA RÉVISION PORTE SON ProductId EN CLAIR, ELLE.
                //
                // Contrairement aux variantes et aux médias, dont la clé étrangère
                // est une propriété fantôme. C'est ce qui rend ce cas plus simple —
                // et c'est aussi pourquoi il fallait l'ajouter séparément : le
                // `switch` ci-dessus ne l'aurait pas attrapée, et modifier le nom
                // d'un produit aurait laissé l'ancien en cache pour tout le TTL.
                case ProductRevision revision:
                    touchedProducts.Add(revision.ProductId.Value);
                    break;

                // ProductCondition et ProductDefect n'ont pas besoin de cas propre :
                // ils ne changent qu'à travers ProductRevision.Remplacer, qui marque
                // la révision comme modifiée dans la même unité de travail.

                // ═════════════════════════════════════════════════════════════
                // LA FICHE TECHNIQUE, ELLE, A BESOIN DU SIEN — ET LE
                //    RAISONNEMENT CI-DESSUS EXPLIQUE POURQUOI.
                //
                // « La révision est marquée comme modifiée dans la même unité de
                // travail » n'est vrai que si un CHAMP de la révision change. Or
                // le vendeur peut ne toucher QUE ses caractéristiques : le
                // formulaire renvoie le même nom, le même prix, la même
                // description, et une fiche technique différente. EF ne voit alors
                // aucune modification sur `product_revisions` — la révision reste
                // « Unchanged », le cas au-dessus ne se déclenche pas, et la fiche
                // en cache garde son ancienne fiche technique pendant tout le TTL.
                //
                // Le lien vers le produit demande deux sauts (ligne → groupe →
                // révision), et c'est le prix de la table séparée. Les entités
                // traversées sont forcément suivies : elles ne se modifient qu'à
                // travers `ProductRevision.RemplacerSpecifications`, qui part de
                // l'agrégat chargé.
                // ═════════════════════════════════════════════════════════════
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

            // La liste de la boutique doit tomber elle aussi. Les enfants ne portent
            // pas le SellerId : on le relit sur l'agrégat, forcément suivi puisque
            // les enfants ne se modifient qu'à travers lui.
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
    /// ChangeTracker — aucune requête, on est en plein <c>SaveChanges</c>.
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

    /// <summary>
    /// Lit la clé étrangère « ProductId » d'une variante ou d'un média. C'est une
    /// propriété SHADOW (déclarée dans ProductConfiguration, absente du modèle de
    /// domaine) : elle n'existe que dans le ChangeTracker.
    /// </summary>
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
        // fortement typé (`readonly record struct ProductId(Guid Value)`). Dans le
        // ChangeTracker sa valeur CLR est donc un ProductId, PAS un Guid nu — ne
        // tester que `is Guid` renvoyait toujours false, la fiche produit n'était
        // jamais purgée après un ajout de variante/média sur un produit existant,
        // et le cache servait 0 variante pendant tout le TTL. On accepte les deux
        // formes par prudence (une future config pourrait exposer le Guid brut).
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
