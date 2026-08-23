using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application;
using HBA.Catalog.Application.Products;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Brands;
using HBA.Catalog.Domain.Categories;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Infrastructure.Persistence;

namespace HBA.Catalog.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module. Lecture seule,
/// AsNoTracking, projetée vers les DTOs de Contracts. Les autres modules ne
/// voient que ça du catalogue.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// C'EST LE CHEMIN LE PLUS CHAUD DE L'APPLICATION.
///
/// La fiche produit mobile (/mobile/products/{id}, anonyme) passe par ici à chaque
/// affichage. Toutes les lectures sont en cache-aside, et partagent leurs clés avec
/// les query handlers correspondants : une même fiche n'est lue en base qu'une fois
/// pour les deux chemins.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
internal sealed class CatalogModuleApi : ICatalogModuleApi
{
    private readonly CatalogDbContext _dbContext;
    private readonly ICacheService _cache;

    public CatalogModuleApi(CatalogDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Product(productId),
            async ct =>
            {
                var id = new ProductId(productId);

                // `Include(Revisions)` EST OBLIGATOIRE : sans lui,
                // `Product.CurrentRevision` lève. Voir l'encadré de ProductRepository.
                var product = await _dbContext.Products
                    .AsNoTracking()
                    .Include(p => p.Revisions).ThenInclude(r => r.Condition).ThenInclude(c => c.Defects)
                    .Include(p => p.Revisions).ThenInclude(r => r.Specifications).ThenInclude(g => g.Items)
                    .Include(p => p.Variants)
                    .Include(p => p.Media)
                    .FirstOrDefaultAsync(p => p.Id == id, ct);

                // VUE VENDEUR, ET C'EST VOULU MALGRÉ LE NOM DE LA CLASSE.
                //
                // `ICatalogModuleApi.GetProductAsync` sert deux appelants très
                // différents : la garde d'appartenance des endpoints vendeur
                // (`DenyUnlessProductOwnerAsync`), qui doit voir la fiche même en
                // brouillon, et la fiche mobile. La première cesserait de
                // fonctionner sur un produit non publié — le vendeur perdrait
                // l'accès à son propre brouillon avec un 404.
                //
                // La séparation propre est l'affaire de l'API publique du §17, qui
                // appellera `ToPublicSummary`. Tant qu'elle n'existe pas, ce chemin
                // reste la vue vendeur, et il ne doit PAS être branché tel quel sur
                // une route anonyme.
                return product is null ? null : ProductMapping.ToSellerSummary(product);
            },
            CatalogCacheKeys.ProductTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Brand(brandId),
            async ct =>
            {
                var id = new BrandId(brandId);

                var brand = await _dbContext.Brands
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == id, ct);

                return brand is null
                    ? null
                    : new BrandSummary(brand.Id.Value, brand.Name, brand.Slug.Value, brand.Status.ToString(), brand.LogoUrl, brand.Description);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            CatalogCacheKeys.Category(categoryId),
            async ct =>
            {
                var id = new CategoryId(categoryId);

                var category = await _dbContext.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);

                return category is null
                    ? null
                    : new CategorySummary(category.Id.Value, category.ParentId, category.Name, category.Slug.Value, category.Path, category.Status.ToString(), category.ImageUrl, category.AttributeSchema);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

    public async Task<IReadOnlyList<ProductSummary>> ListFeaturedAsync(int max, CancellationToken cancellationToken = default)
    {
        var limit = max is < 1 or > 50 ? 12 : max;

        // ═════════════════════════════════════════════════════════════════════
        // LE TAG EST LU SUR LA RÉVISION PUBLIÉE, PAS SUR LA COURANTE.
        //
        // Cette liste alimente la VITRINE. La lire sur la révision courante
        // mettrait en avant une fiche dont un vendeur vient d'ajouter le tag
        // « featured » à un brouillon que personne n'a validé — la mise en avant
        // deviendrait libre-service.
        //
        // `Tags` est un text[] natif : le filtre se traduit en
        // `'featured' = ANY(tags)` côté PostgreSQL (index GIN possible).
        // ═════════════════════════════════════════════════════════════════════
        var ids = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.Published
                        && _dbContext.ProductRevisions.Any(r =>
                            r.Id == p.PublishedRevisionId && r.Tags.Contains("featured")))
            .OrderByDescending(p => p.PublishedAtUtc)
            .Take(limit)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return Array.Empty<ProductSummary>();
        }

        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Revisions).ThenInclude(r => r.Condition).ThenInclude(c => c.Defects)
            .Include(p => p.Revisions).ThenInclude(r => r.Specifications).ThenInclude(g => g.Items)
            .Include(p => p.Variants)
            .Include(p => p.Media)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var parId = products.ToDictionary(p => p.Id);

        return ids
            .Where(parId.ContainsKey)
            .Select(id => ProductMapping.ToPublicSummary(parId[id]))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }
}
