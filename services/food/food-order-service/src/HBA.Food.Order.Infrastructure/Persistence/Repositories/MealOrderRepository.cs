using HBA.FoodOrders.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HBA.FoodOrders.Infrastructure.Persistence;

internal sealed class MealOrderRepository : IMealOrderRepository
{
    private readonly MealOrderingDbContext _dbContext;

    public MealOrderRepository(MealOrderingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(MealOrder order, CancellationToken cancellationToken = default)
        => await _dbContext.Orders.AddAsync(order, cancellationToken);

    /// <summary>
    /// LES OPTIONS SE CHARGENT AVEC LES LIGNES.
    ///
    /// Sans `ThenInclude`, la commande part en cuisine sans les choix du client —
    /// un « riz poulet » devient un « riz », et personne ne s'en aperçoit avant
    /// que le sac ne soit ouvert.
    /// </summary>
    public async Task<MealOrder?> GetByIdAsync(
        MealOrderId id, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <remarks>
    /// MÊME DÉFAUT QUE L'HISTORIQUE MARKETPLACE, SANS MÊME SON `AsSplitQuery` :
    /// tout l'historique de l'acheteur, avec les lignes et les options de chaque
    /// commande. Le `Take` porte sur la requête racine — les `Include` ne
    /// multiplient pas les commandes rapatriées.
    /// </remarks>
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

    /// <remarks>
    /// LE CARNET D'UN RESTAURANT CROÎT PLUS VITE QUE CELUI D'UN ACHETEUR — c'est
    /// un établissement, pas une personne. Il n'avait pas plus de borne.
    /// </remarks>
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

    /// <summary>
    /// SANS SUIVI ET SANS `Include` : C'EST UN EXISTS SUR INDEX.
    ///
    /// Appelée à CHAQUE valorisation de panier. Charger les lignes et leurs
    /// options de toutes les commandes d'un client pour répondre « oui » ou
    /// « non » coûterait cent fois la question.
    /// </summary>
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
