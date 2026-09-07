using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Partners;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Persistence;

internal sealed class PartnerRepository : IPartnerRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public PartnerRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Partner?> GetByIdAsync(PartnerId id, CancellationToken cancellationToken = default)
        => await _dbContext.Partners
            .Include(p => p.ApiKeys)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Partner?> FindByApiKeyPrefixAsync(
        string prefix, CancellationToken cancellationToken = default)
        // LA REQUÊTE DU CHEMIN D'AUTHENTIFICATION.
        => await _dbContext.Partners
            .Include(p => p.ApiKeys)
            .FirstOrDefaultAsync(
                p => p.ApiKeys.Any(k => k.Prefix == prefix && k.RevokedAtUtc == null),
                cancellationToken);

    public async Task<IReadOnlyList<Partner>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Partners
            .AsNoTracking()
            .Include(p => p.ApiKeys)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<int> CountDeliveriesTodayAsync(
        PartnerId id, CancellationToken cancellationToken = default)
    {
        // Journée en UTC, et c'est un choix à connaître : le Bénin est à UTC+1,
        // donc un quota « quotidien » se réinitialise à 1 h du matin locale.
        var since = DateTime.UtcNow.Date;

        return await _dbContext.Deliveries
            .AsNoTracking()
            .CountAsync(d => d.PartnerId == id.Value && d.CreatedAtUtc >= since, cancellationToken);
    }

    public async Task AddAsync(Partner partner, CancellationToken cancellationToken = default)
        => await _dbContext.Partners.AddAsync(partner, cancellationToken);
}
