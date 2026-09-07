using Microsoft.EntityFrameworkCore;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Infrastructure.Persistence;

internal sealed class SellerRepository : ISellerRepository
{
    private readonly SellersDbContext _dbContext;

    public SellerRepository(SellersDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Seller seller, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AddAsync(seller, cancellationToken);

    public void Remove(Seller seller)
        => _dbContext.Sellers.Remove(seller);

    public async Task<Seller?> GetByIdAsync(SellerId id, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<Seller?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AnyAsync(s => s.UserId == userId, cancellationToken);

    public async Task<bool> ShopNameExistsAsync(string shopName, CancellationToken cancellationToken = default)
        => await _dbContext.Sellers.AnyAsync(s => s.ShopName == shopName, cancellationToken);

    public async Task<(IReadOnlyList<Seller> Items, int Total, IReadOnlyDictionary<string, int> KybFacets)>
        ListPagedAsync(
            int page,
            int pageSize,
            string? search,
            KybStatus? kybStatus,
            SellerStatus? status,
            CancellationToken cancellationToken = default)
    {
        var recherche = _dbContext.Sellers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var motif = $"%{search.Trim()}%";

            // `EF.Functions.ILike` ET NON `.ToLower().Contains()`.
            recherche = recherche.Where(s => EF.Functions.ILike(s.ShopName, motif));
        }

        // LES FACETTES SE COMPTENT AVANT LE FILTRE DE STATUT KYB.
        var facettes = await recherche
            .GroupBy(s => s.KybStatus)
            .Select(g => new { Statut = g.Key, Compte = g.Count() })
            .ToDictionaryAsync(x => x.Statut.ToString(), x => x.Compte, cancellationToken);

        if (kybStatus is { } kyb)
        {
            recherche = recherche.Where(s => s.KybStatus == kyb);
        }

        if (status is { } etat)
        {
            recherche = recherche.Where(s => s.Status == etat);
        }

        var total = await recherche.CountAsync(cancellationToken);

        // `AsSplitQuery` EST OBLIGATOIRE DÈS QU'ON PAGINE AVEC UN `.Include`.
        IReadOnlyList<Seller> vendeurs = await recherche
            .Include(s => s.KybDocuments)
            .AsSplitQuery()
            .OrderBy(s => s.CreatedOnUtc)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (vendeurs, total, facettes);
    }
}
