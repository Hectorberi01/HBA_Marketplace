using Microsoft.EntityFrameworkCore;
using HBA.Commerce.Domain.Carts;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Infrastructure.Persistence;

internal sealed class CartRepository : ICartRepository
{
    private readonly CartDbContext _dbContext;

    public CartRepository(CartDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CartAggregate cart, CancellationToken cancellationToken = default)
        => await _dbContext.Carts.AddAsync(cart, cancellationToken);

    /// <summary>
    /// LES OPTIONS SONT CHARGÉES AVEC LES LIGNES, ET IL LE FAUT.
    ///
    /// Sans `ThenInclude`, une ligne food revient avec une collection d'options
    /// VIDE. Trois conséquences, toutes silencieuses : `MatchesFood` croirait que
    /// tout plat sans option correspond et fusionnerait « riz nature » avec « riz
    /// poulet » ; le panier afficherait un prix incluant des suppléments
    /// invisibles ; et la commande partirait en cuisine sans les choix du client.
    /// </summary>
    public async Task<CartAggregate?> GetByIdAsync(CartId id, CancellationToken cancellationToken = default)
        => await _dbContext.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Options)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CartAggregate?> GetActiveByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => await _dbContext.Carts
            .Include(c => c.Items)
                .ThenInclude(i => i.Options)
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.Status == CartStatus.Active, cancellationToken);
}
