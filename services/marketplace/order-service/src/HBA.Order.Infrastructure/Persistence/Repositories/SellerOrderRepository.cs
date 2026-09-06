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

    /// <summary>
    /// AVEC SES LIGNES, PARCE QUE LE REFUS EN A BESOIN.
    ///
    /// `SellerOrder.Reject` et `SellerOrder.Cancel` construisent l'événement de
    /// refus à partir de `Lines` — SKU, emplacement d'expédition, montant. Sans
    /// l'`Include`, la collection serait VIDE sans qu'aucune exception ne le
    /// signale, et l'événement partirait sans une seule ligne : son futur
    /// consommateur ne saurait ni quel stock rendre, ni combien rembourser, et
    /// rien n'aurait échoué.
    ///
    /// C'est la même classe de défaut que l'`Include` des dossiers de retour dans
    /// `OrderRepository.GetByIdAsync` — un chargement manquant qui ne se voit qu'à
    /// la lecture d'un champ qu'on croit rempli.
    /// </summary>
    public async Task<SellerOrder?> FindAsync(
        Guid orderId, Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.OrderId == orderId && s.SellerId == sellerId, cancellationToken);

    /// <summary>
    /// SUIVIES, PAS EN `AsNoTracking` : c'est cette lecture que
    /// `CancelSellerOrdersOnOrderCancelledHandler` mute pour fermer les parts
    /// d'une commande annulée. En lecture seule, ses écritures seraient perdues en
    /// silence.
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
    /// <remarks>
    /// LA BORNE DOIT ÊTRE LA MÊME QUE CELLE DE `IOrderRepository.ListBySellerAsync`.
    ///
    /// `ListOrdersBySellerQueryHandler` appelle les deux et joint les résultats par
    /// `OrderId`. Si ce carnet-ci était borné plus court, des commandes remontées
    /// par l'autre lecture perdraient leur état vendeur — silencieusement, et
    /// seulement pour les plus anciennes. C'est l'appelant qui passe la même valeur
    /// aux deux ; ce défaut serait invisible en test avec peu de données.
    /// </remarks>
    public async Task<IReadOnlyList<SellerOrder>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders
            .AsNoTracking()
            .Include(s => s.Lines)
            .Where(s => s.SellerId == sellerId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    // Pas d'Include, pas de ToList : un EXISTS servi par l'index unique
    // (OrderId, SellerId). Cette lecture est sur le chemin de CHAQUE confirmation
    // de paiement — elle doit rester à quelques microsecondes.
    public async Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerOrders.AnyAsync(s => s.OrderId == orderId, cancellationToken);
}
