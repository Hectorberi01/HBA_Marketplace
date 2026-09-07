using HBA.FoodOrders.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HBA.FoodOrders.Infrastructure.Persistence;

internal sealed class MealOrderRepository : IMealOrderRepository
{
    private readonly MealOrderingDbContext _dbContext;

    public MealOrderRepository(MealOrderingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(MealOrder order, CancellationToken cancellationToken = default)
        => await _dbContext.Orders.AddAsync(order, cancellationToken);

    /// <summary>LES OPTIONS SE CHARGENT AVEC LES LIGNES.</summary>
    public async Task<MealOrder?> GetByIdAsync(
        MealOrderId id, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MealOrder>> ListByBuyerAsync(
        Guid buyerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MealOrder>> ListByRestaurantAsync(
        Guid restaurantId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Where(o => o.RestaurantId == restaurantId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    /// <summary>SANS SUIVI ET SANS `Include` : C'EST UN EXISTS SUR INDEX.</summary>
    public Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => _dbContext.Orders
            .AsNoTracking()
            .AnyAsync(
                o => o.BuyerId == buyerId
                    && (o.Status == MealOrderStatus.Paid
                        || o.Status == MealOrderStatus.Confirmed
                        || o.Status == MealOrderStatus.Delivered),
                cancellationToken);

    public async Task<MealOrder?> GetByCartAsync(
        Guid cartId, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .FirstOrDefaultAsync(o => o.CartId == cartId, cancellationToken);
}
