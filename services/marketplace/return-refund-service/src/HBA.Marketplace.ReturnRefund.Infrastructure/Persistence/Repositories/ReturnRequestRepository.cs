using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Repositories;

internal sealed class ReturnRequestRepository : IReturnRequestRepository
{
    private readonly ReturnRefundDbContext _db;

    public ReturnRequestRepository(ReturnRefundDbContext db) => _db = db;

    public Task<ReturnRequest?> GetAsync(Guid id, CancellationToken cancellationToken)
        => Query().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<ReturnRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var key = await _db.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Key == idempotencyKey, cancellationToken);

        return key is null ? null : await GetAsync(key.ReturnRequestId, cancellationToken);
    }

    public async Task AddAsync(ReturnRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        await _db.ReturnRequests.AddAsync(request, cancellationToken);
        await _db.IdempotencyKeys.AddAsync(new ReturnIdempotencyKey(idempotencyKey, request.Id, request.CreatedAtUtc), cancellationToken);
    }

    public async Task<IReadOnlyList<ReturnRequest>> ListCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken)
        => await Query().Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ListSellerAsync(Guid sellerId, int page, int pageSize, CancellationToken cancellationToken)
        => await Query().Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<int> CountCustomerAsync(Guid customerId, CancellationToken cancellationToken)
        => await _db.ReturnRequests.AsNoTracking().CountAsync(r => r.CustomerId == customerId, cancellationToken);

    public async Task<int> CountSellerAsync(Guid sellerId, CancellationToken cancellationToken)
        => await _db.ReturnRequests.AsNoTracking().CountAsync(r => r.SellerId == sellerId, cancellationToken);

    public async Task<(IReadOnlyList<ReturnRequest> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForAdminAsync(int page, int pageSize, ReturnStatus? status, CancellationToken cancellationToken)
    {
        var nu = _db.ReturnRequests.AsNoTracking();

        // Facettes calculées AVANT le filtre : elles disent ce qu'il y a ailleurs.
        var comptes = await nu
            .GroupBy(r => r.Status)
            .Select(g => new { Statut = g.Key, Nombre = g.Count() })
            .ToListAsync(cancellationToken);

        var filtre = status is { } etat ? nu.Where(r => r.Status == etat) : nu;
        var total = await filtre.CountAsync(cancellationToken);

        var page_ = Query();

        if (status is { } retenu)
        {
            page_ = page_.Where(r => r.Status == retenu);
        }

        var elements = await page_
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (elements, total, comptes.ToDictionary(x => x.Statut.ToString(), x => x.Nombre));
    }

    /// <summary>PROJECTION `AsNoTracking`, ET NON CHARGEMENT DES AGRÉGATS.</summary>
    public async Task<IReadOnlyList<RefundExecutionTicket>> ListRefundsAwaitingExecutionAsync(
        int batchSize, CancellationToken cancellationToken)
        => await (
                // LA JOINTURE SUR LE DOSSIER N'EST PAS DÉCORATIVE.
                from remboursement in _db.Set<Refund>().AsNoTracking()
                join dossier in _db.ReturnRequests.AsNoTracking()
                    on remboursement.ReturnId equals dossier.Id
                where dossier.Status == ReturnStatus.RefundPending
                    && (remboursement.Status == RefundStatus.Pending
                        || remboursement.Status == RefundStatus.Processing
                        || remboursement.Status == RefundStatus.Failed)
                orderby remboursement.CreatedAtUtc
                select new RefundExecutionTicket(remboursement.ReturnId, remboursement.Id))
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnRequest>> ListExpirableAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken)
        => await Query()
            .Where(r => r.ExpiresAtUtc < nowUtc
                && (r.Status == ReturnStatus.AwaitingApproval || r.Status == ReturnStatus.AwaitingReturn))
            .OrderBy(r => r.ExpiresAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// PROJECTION, PAS AGRÉGATS. On additionne des quantités ; charger les dossiers
    /// entiers ramènerait pièces jointes, expéditions, inspections et historique
    /// pour en lire deux colonnes.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> ListOpenQuantitiesByOrderAsync(
        Guid orderId, Guid? exceptReturnId, CancellationToken cancellationToken)
    {
        var lignes = await (
                from item in _db.Set<ReturnItem>().AsNoTracking()
                join dossier in _db.ReturnRequests.AsNoTracking()
                    on item.ReturnId equals dossier.Id
                where dossier.OrderId == orderId
                    && (exceptReturnId == null || dossier.Id != exceptReturnId)
                    && dossier.Status != ReturnStatus.Refunded
                    && dossier.Status != ReturnStatus.Closed
                    && dossier.Status != ReturnStatus.Rejected
                    && dossier.Status != ReturnStatus.RejectedAfterInspection
                    && dossier.Status != ReturnStatus.Cancelled
                    && dossier.Status != ReturnStatus.Expired
                select new
                {
                    item.OrderItemId,
                    Quantite = item.ReceivedQuantity > 0 ? item.ReceivedQuantity : item.RequestedQuantity
                })
            .ToListAsync(cancellationToken);

        return lignes
            .GroupBy(l => l.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantite));
    }

    /// <summary>
    /// Voir l'encadré de <c> IReturnRequestRepository.GetOrderSummaryAsync</c> pour
    /// les deux décisions de comptage.
    /// </summary>
    public async Task<(decimal MontantRembourse, string Devise, int DossiersActifs)> GetOrderSummaryAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        // L'ARGENT PARTI, PAS L'ARGENT PROMIS : `Succeeded` uniquement.
        var reussis = await (
                from remboursement in _db.Set<Refund>().AsNoTracking()
                join dossier in _db.ReturnRequests.AsNoTracking()
                    on remboursement.ReturnId equals dossier.Id
                where dossier.OrderId == orderId
                    && remboursement.Status == RefundStatus.Succeeded
                select new { remboursement.Amount, remboursement.Currency })
            .ToListAsync(cancellationToken);

        var actifs = await _db.ReturnRequests.AsNoTracking()
            .CountAsync(
                dossier => dossier.OrderId == orderId
                    && dossier.Status != ReturnStatus.Refunded
                    && dossier.Status != ReturnStatus.Closed
                    && dossier.Status != ReturnStatus.Rejected
                    && dossier.Status != ReturnStatus.RejectedAfterInspection
                    && dossier.Status != ReturnStatus.Cancelled
                    && dossier.Status != ReturnStatus.Expired,
                cancellationToken);

        // MONODEVISE ASSUMÉE. On additionne sans regarder la devise et on rend
        // celle du premier remboursement — voir « ce que cette lecture ne couvre
        // pas » dans l'interface.
        var montant = reussis.Sum(r => r.Amount);
        var devise = reussis.Count > 0 ? reussis[0].Currency : "XOF";

        return (montant, devise, actifs);
    }

    private IQueryable<ReturnRequest> Query()
        => _db.ReturnRequests
            .Include(r => r.Items)
            .Include(r => r.Evidence)
            .Include(r => r.Shipments)
            .Include(r => r.Inspections)
            .Include(r => r.Refunds)
            .ThenInclude(r => r.Attempts)
            .Include(r => r.History);
}
