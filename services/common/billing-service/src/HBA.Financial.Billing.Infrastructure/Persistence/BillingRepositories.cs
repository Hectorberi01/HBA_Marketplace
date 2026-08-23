using Microsoft.EntityFrameworkCore;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;

namespace HBA.Financial.Billing.Infrastructure.Persistence;

internal sealed class CommissionRuleRepository : ICommissionRuleRepository
{
    private readonly BillingDbContext _dbContext;

    public CommissionRuleRepository(BillingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CommissionRule rule, CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules.AddAsync(rule, cancellationToken);

    public void Remove(CommissionRule rule) => _dbContext.CommissionRules.Remove(rule);

    public async Task<CommissionRule?> GetByIdAsync(CommissionRuleId id, CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CommissionRule>> GetCandidatesAsync(Guid sellerId, Guid categoryId, CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules
            .Where(r => r.IsActive &&
                        (r.Scope == CommissionScope.Global
                         || (r.Scope == CommissionScope.Seller && r.TargetId == sellerId)
                         || (r.Scope == CommissionScope.Category && r.TargetId == categoryId)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CommissionRule>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.CommissionRules.OrderByDescending(r => r.EffectiveFromUtc).ToListAsync(cancellationToken);
}

internal sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly BillingDbContext _dbContext;

    public InvoiceRepository(BillingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
        => await _dbContext.Invoices.AddAsync(invoice, cancellationToken);

    public async Task<Invoice?> GetByIdAsync(InvoiceId id, CancellationToken cancellationToken = default)
        => await _dbContext.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Invoices
            .Where(i => i.SellerId == sellerId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .Include(i => i.Lines)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// LE COMPTE ET LES FACETTES NE CHARGENT PAS LES LIGNES.
    ///
    /// `Include(i => i.Lines)` est nécessaire pour AFFICHER une facture, absurde
    /// pour en COMPTER : sur toute la table, la jointure produirait autant de
    /// lignes qu'il y a de postes facturés, rien que pour rendre un entier.
    /// La page, elle, les garde — le résumé rendu porte le total et le statut,
    /// et le détail se lit sur la fiche.
    ///
    /// LE FILTRE VENDEUR EST OPTIONNEL ET NE REMPLACE PAS `ListBySellerAsync`.
    ///
    /// Celle-ci est servie par une route ADMIN. `ListBySellerAsync` est servie
    /// par une route où un vendeur passe la garde d'appartenance sur SON dossier.
    /// Les fondre donnerait une seule route à deux régimes d'autorisation, et
    /// c'est ainsi qu'on ouvre une fuite sans s'en apercevoir.
    /// </remarks>
    public async Task<(IReadOnlyList<Invoice> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForAdminAsync(
            int page, int pageSize, InvoiceStatus? status, Guid? sellerId,
            CancellationToken cancellationToken = default)
    {
        var nu = _dbContext.Invoices.AsNoTracking();

        if (sellerId is { } vendeur)
        {
            nu = nu.Where(i => i.SellerId == vendeur);
        }

        var comptes = await nu
            .GroupBy(i => i.Status)
            .Select(g => new { Statut = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var filtre = status is { } etat ? nu.Where(i => i.Status == etat) : nu;

        var total = await filtre.CountAsync(cancellationToken);

        var elements = await filtre
            .OrderByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(i => i.Lines)
            .ToListAsync(cancellationToken);

        return (elements, total, comptes.ToDictionary(x => x.Statut.ToString(), x => x.Nombre));
    }
}
