using Microsoft.EntityFrameworkCore;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Persistence;

internal sealed class SellerEarningRepository : ISellerEarningRepository
{
    private readonly WalletDbContext _dbContext;

    public SellerEarningRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SellerEarning earning, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings.AddAsync(earning, cancellationToken);

    public async Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings.AnyAsync(e => e.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListAccruedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.Status == EarningStatus.Accrued && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListReleasedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        // On filtre sur la date de LIBÉRATION (ReleasedAtUtc = quand le gain est
        // devenu payable), et NON sur la date de création/confirmation : c'est la
        // période de règlement qui compte.
        => await _dbContext.Earnings
            .Where(e => e.Status == EarningStatus.Released
                        && e.ReleasedAtUtc != null
                        && e.ReleasedAtUtc >= startUtc
                        && e.ReleasedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

    // SUIVI activé : l'annulation d'un lot mute ces entités (retour à « Released
    // »).
    public async Task<IReadOnlyList<SellerEarning>> ListByBatchAsync(Guid settlementBatchId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.SettlementBatchId == settlementBatchId)
            .ToListAsync(cancellationToken);

    // SUIVI activé : l'imputation d'un retrait mute ces entités (passage à «
    // Settled »).
    public async Task<IReadOnlyList<SellerEarning>> ListReleasedBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.SellerId == sellerId && e.Status == EarningStatus.Released)
            .OrderBy(e => e.ReleasedAtUtc)
            .ThenBy(e => e.CreatedAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    // SUIVI activé : un retrait refusé ou échoué remet ces gains en « Released ».
    public async Task<IReadOnlyList<SellerEarning>> ListByWithdrawalAsync(Guid withdrawalId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.SettledByWithdrawalId == withdrawalId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerEarning>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .Where(e => e.OrderId == orderId)
            .ToListAsync(cancellationToken);

    public async Task<SellerStatement> GetSellerStatementAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var lines = await _dbContext.Earnings
            .AsNoTracking()
            .Where(e => e.SellerId == sellerId && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .ToListAsync(cancellationToken);

        var currency = lines.Count > 0 ? lines[0].Currency : "XOF";

        // LE RELEVÉ EST NET DES REPRISES — IL NE L'ÉTAIT PAS.
        return new SellerStatement(
            sellerId,
            lines.Sum(e => e.RemainingGrossAmount),
            lines.Sum(e => e.RemainingCommissionAmount),
            lines.Sum(e => e.RemainingProviderFeeAmount),
            lines.Sum(e => e.RemainingNetAmount),
            currency,
            lines.Count);
    }

    public async Task<IReadOnlyList<SellerEarning>> ListSellerEarningsAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => await _dbContext.Earnings
            .AsNoTracking()
            .Where(e => e.SellerId == sellerId && e.CreatedAtUtc >= startUtc && e.CreatedAtUtc <= endUtc)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}

internal sealed class SettlementBatchRepository : ISettlementBatchRepository
{
    private readonly WalletDbContext _dbContext;

    public SettlementBatchRepository(WalletDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SettlementBatch batch, CancellationToken cancellationToken = default)
        => await _dbContext.Batches.AddAsync(batch, cancellationToken);

    public async Task<SettlementBatch?> GetByIdAsync(SettlementBatchId id, CancellationToken cancellationToken = default)
        => await _dbContext.Batches.Include(b => b.Payouts).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SettlementBatch>> ListAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Batches
            .Include(b => b.Payouts)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>Le plafond de lecture des versements d'un vendeur.</summary>
    private const int PlafondDeVersements = 200;

    /// <summary>
    /// Les versements d'UN vendeur, ordonnés du lot le plus récent au plus ancien.
    /// </summary>
    public async Task<IReadOnlyList<Payout>> ListPayoutsBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var page = await _dbContext.Batches
            .AsNoTracking()
            .SelectMany(
                b => b.Payouts.Where(p => p.SellerId == sellerId),
                (b, p) => new { b.CreatedAtUtc, Versement = p })
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(PlafondDeVersements)
            .ToListAsync(cancellationToken);

        return page.Select(x => x.Versement).ToList();
    }
}
