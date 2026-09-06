using Microsoft.EntityFrameworkCore;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Brands;

namespace HBA.Catalog.Infrastructure.Persistence;

internal sealed class AttributeDefinitionRepository : IAttributeDefinitionRepository
{
    private readonly CatalogDbContext _dbContext;

    public AttributeDefinitionRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(AttributeDefinition definition, CancellationToken cancellationToken = default)
        => await _dbContext.AttributeDefinitions.AddAsync(definition, cancellationToken);

    public async Task<AttributeDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbContext.AttributeDefinitions.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<AttributeDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // La normalisation doit être la MÊME qu'à la création, sinon le contrôle
        // d'unicité laisse passer « Color » quand « color » existe déjà.
        var normalise = (code ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');

        return await _dbContext.AttributeDefinitions
            .FirstOrDefaultAsync(a => a.Code == normalise, cancellationToken);
    }

    public async Task<IReadOnlyList<AttributeDefinition>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.AttributeDefinitions
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
}

internal sealed class CategoryAttributeRepository : ICategoryAttributeRepository
{
    private readonly CatalogDbContext _dbContext;

    public CategoryAttributeRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CategoryAttribute attribute, CancellationToken cancellationToken = default)
        => await _dbContext.CategoryAttributes.AddAsync(attribute, cancellationToken);

    public void Remove(CategoryAttribute attribute)
        => _dbContext.CategoryAttributes.Remove(attribute);

    public async Task<CategoryAttribute?> GetAsync(
        Guid categoryId, Guid attributeDefinitionId, CancellationToken cancellationToken = default)
        => await _dbContext.CategoryAttributes
            .FirstOrDefaultAsync(
                a => a.CategoryId == categoryId && a.AttributeDefinitionId == attributeDefinitionId,
                cancellationToken);

    public async Task<IReadOnlyList<AttributDeCategorie>> ListByCategoryAsync(
        Guid categoryId, CancellationToken cancellationToken = default)
    {
        // UNE JOINTURE EXPLICITE, PAS UNE NAVIGATION.
        //
        // Il n'y a délibérément aucune propriété de navigation entre
        // `CategoryAttribute` et `AttributeDefinition` : une définition est partagée
        // par des dizaines de catégories, et une navigation inverse ferait charger
        // ces dizaines de rattachements à chaque lecture d'un attribut.
        var lignes = await _dbContext.CategoryAttributes
            .AsNoTracking()
            .Where(a => a.CategoryId == categoryId)
            .Join(_dbContext.AttributeDefinitions.AsNoTracking(),
                  a => a.AttributeDefinitionId,
                  d => d.Id,
                  (a, d) => new { Rattachement = a, Definition = d })
            .OrderBy(x => x.Rattachement.DisplayOrder)
            .ToListAsync(cancellationToken);

        return lignes
            .Select(x => new AttributDeCategorie(x.Definition, x.Rattachement))
            .ToList();
    }
}

internal sealed class BrandRequestRepository : IBrandRequestRepository
{
    private readonly CatalogDbContext _dbContext;

    public BrandRequestRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(BrandRequest request, CancellationToken cancellationToken = default)
        => await _dbContext.BrandRequests.AddAsync(request, cancellationToken);

    public async Task<BrandRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbContext.BrandRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BrandRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
        => await _dbContext.BrandRequests
            .AsNoTracking()
            .Where(r => r.Status == BrandRequestStatus.Pending)
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<BrandRequest?> GetPendingByNameAsync(
        Guid sellerId, string name, CancellationToken cancellationToken = default)
    {
        var normalise = (name ?? string.Empty).Trim();

        return await _dbContext.BrandRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.SellerId == sellerId
                     && r.Status == BrandRequestStatus.Pending
                     && r.Name.ToLower() == normalise.ToLower(),
                cancellationToken);
    }
}
