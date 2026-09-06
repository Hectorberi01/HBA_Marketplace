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
            //
            // Le second traduit en `lower(...) LIKE ...`, ce qui écarte l'index de
            // `ShopName` — et cette colonne en porte un, unique. `ILike` est
            // l'opérateur natif de PostgreSQL, et il reste indexable.
            recherche = recherche.Where(s => EF.Functions.ILike(s.ShopName, motif));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LES FACETTES SE COMPTENT AVANT LE FILTRE DE STATUT KYB.
        //
        // Après, la facette sélectionnée serait la seule non nulle et toutes les
        // autres afficheraient zéro — la console dirait « aucun dossier en revue »
        // au modérateur qui vient justement de filtrer sur « vérifié ». Elles
        // suivent donc la recherche, et elle seule.
        // ═════════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // `AsSplitQuery` EST OBLIGATOIRE DÈS QU'ON PAGINE AVEC UN `.Include`.
        //
        // En requête unique, `Skip`/`Take` s'appliquent aux LIGNES DU JOIN, pas aux
        // vendeurs : un vendeur portant trois pièces consommerait trois places de
        // la page, et la page rendrait sept vendeurs sur vingt demandés. Le défaut
        // ne se voit qu'avec des dossiers inégalement fournis — c'est-à-dire en
        // production, et pas sur un jeu de test où chacun a une pièce.
        //
        // ET L'ORDRE DOIT ÊTRE TOTAL. `CreatedOnUtc` seul ne départage pas deux
        // inscriptions de la même milliseconde ; en requête scindée, les deux
        // requêtes pourraient alors ne pas s'accorder sur la frontière de page, et
        // un vendeur apparaîtrait sur deux pages ou sur aucune. L'identifiant
        // tranche.
        // ═════════════════════════════════════════════════════════════════════
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
