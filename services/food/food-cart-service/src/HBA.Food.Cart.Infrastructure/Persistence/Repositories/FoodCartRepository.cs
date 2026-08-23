using HBA.FoodCarts.Domain.Carts;
using Microsoft.EntityFrameworkCore;
using CartAggregate = HBA.FoodCarts.Domain.Carts.FoodCart;

namespace HBA.FoodCarts.Infrastructure.Persistence;

internal sealed class FoodCartRepository : IFoodCartRepository
{
    private readonly FoodCartDbContext _dbContext;

    public FoodCartRepository(FoodCartDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CartAggregate cart, CancellationToken cancellationToken = default)
        => await _dbContext.Carts.AddAsync(cart, cancellationToken);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES OPTIONS SE CHARGENT AVEC LES LIGNES, ET IL LE FAUT.
    ///
    /// Sans `ThenInclude`, une ligne revient avec une collection d'options VIDE.
    /// Trois conséquences, toutes silencieuses :
    ///
    ///   • `FoodCartItem.Matches` croirait que tout plat sans option correspond,
    ///     et fusionnerait « riz nature » avec « riz poulet » ;
    ///   • le panier afficherait un prix incluant des suppléments invisibles ;
    ///   • la commande partirait en cuisine sans les choix du client.
    ///
    /// Aucune ne lève d'exception. C'est le genre de défaut qu'on découvre à la
    /// réclamation.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<CartAggregate?> GetByIdAsync(
        FoodCartId id, CancellationToken cancellationToken = default)
        => await _dbContext.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Options)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CartAggregate?> GetActiveByBuyerAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
        => await _dbContext.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Options)
            .FirstOrDefaultAsync(
                c => c.BuyerId == buyerId && c.Status == FoodCartStatus.Active, cancellationToken);
}
