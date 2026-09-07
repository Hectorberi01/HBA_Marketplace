using Microsoft.EntityFrameworkCore;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Infrastructure.Persistence;

internal sealed class SellerOrderRepository : ISellerOrderRepository
{
    private readonly OrderingDbContext _dbContext;

    public SellerOrderRepository(OrderingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddRangeAsync(
        IEnumerable<SellerOrder> sellerOrders, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders.AddRangeAsync(sellerOrders, cancellationToken);

    /// <summary>AVEC SES LIGNES, PARCE QUE LE REFUS EN A BESOIN.</summary>
    public async Task<SellerOrder?> FindAsync(
        Guid orderId, Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.OrderId == orderId && s.SellerId == sellerId, cancellationToken);

    /// <summary>
    /// SUIVIES, PAS EN `AsNoTracking` : c'est cette lecture que
    /// `CancelSellerOrdersOnOrderCancelledHandler` mute pour fermer les parts d'une
    /// commande annulée.
    /// </summary>
    public async Task<IReadOnlyList<SellerOrder>> ListByOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders
            .Include(s => s.Lines)
            .Where(s => s.OrderId == orderId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Le carnet d'un vendeur. Lecture pure : elle ne sert qu'à projeter, d'où
    /// l'`AsNoTracking`.
    /// </summary>
    public async Task<IReadOnlyList<SellerOrder>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => s.SellerId == sellerId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    // Pas d'Include, pas de ToList : un EXISTS servi par l'index unique (OrderId,
    // SellerId).
    public async Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders.AnyAsync(s => s.OrderId == orderId, cancellationToken);
}
