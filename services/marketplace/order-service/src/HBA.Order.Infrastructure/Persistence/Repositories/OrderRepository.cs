using Microsoft.EntityFrameworkCore;
using HBA.Orders.Domain.Orders;

namespace HBA.Orders.Infrastructure.Persistence;

internal sealed class OrderRepository : IOrderRepository
{
    private readonly OrderingDbContext _dbContext;

    public OrderRepository(OrderingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
        => await _dbContext.Orders.AddAsync(order, cancellationToken);

    /// <summary>
    /// LES DOSSIERS DE RETOUR SONT CHARGÉS AVEC LA COMMANDE, ET IL LE FAUT.
    ///
    /// C'est cette lecture que sert `GetOrderReturnContextAsync`, la seule source
    /// de return-refund pour savoir ce qui est déjà revenu et déjà remboursé
    /// (ISSUE-014). Sans l'`Include`, la collection serait VIDE sans qu'aucune
    /// exception ne le signale : le contexte repartirait de zéro exactement comme
    /// avant la correction, et le même article se rembourserait indéfiniment.
    ///
    /// `AsSplitQuery` parce qu'il y a désormais DEUX collections imbriquées sous
    /// la commande : une jointure unique multiplierait lignes × options × dossiers
    /// × lignes de dossier.
    /// </summary>
    public async Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .Include(o => o.ReturnSettlements)
                .ThenInclude(s => s.Lines)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    /// <summary>
    /// AVEC SES LIGNES, PARCE QUE L'APPELANT REND CETTE COMMANDE-LÀ.
    ///
    /// `PlaceOrderCommandHandler` s'en sert pour répondre au second appel comme au
    /// premier. Se contenter de l'identifiant suffirait à la réponse HTTP, mais
    /// laisserait un agrégat incomplet suivi par le contexte — et le premier code
    /// qui lirait `order.Lines` y trouverait une collection vide, sans qu'aucune
    /// exception ne le signale.
    /// </summary>
    public async Task<Order?> GetByCartAsync(Guid cartId, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            .FirstOrDefaultAsync(o => o.CartId == cartId, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListByBuyerAsync(
        Guid buyerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            // REQUÊTE ÉCLATÉE : DEUX COLLECTIONS IMBRIQUÉES SUR UNE LISTE.
            //
            // `Lines` puis `Options` en une seule requête ramène
            // commandes × lignes × options lignes SQL, que EF déduplique ensuite
            // côté client. Sur la console d'administration — cinq cents commandes
            // — c'est mesurable. `AsSplitQuery` émet une requête par niveau.
            .AsSplitQuery()
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default)
        // Pas d'Include, pas de ToList : un EXISTS servi par l'index (BuyerId, Status).
        // Cette lecture est sur le chemin chaud (chaque affichage de panier valorisé) —
        // elle doit rester à quelques microsecondes.
        => await _dbContext.Orders
            .AnyAsync(
                o => o.BuyerId == buyerId
                     && (o.Status == OrderStatus.Paid
                         || o.Status == OrderStatus.Confirmed
                         || o.Status == OrderStatus.Delivered),
                cancellationToken);

    public async Task<IReadOnlyList<Order>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            // REQUÊTE ÉCLATÉE : DEUX COLLECTIONS IMBRIQUÉES SUR UNE LISTE.
            //
            // `Lines` puis `Options` en une seule requête ramène
            // commandes × lignes × options lignes SQL, que EF déduplique ensuite
            // côté client. Sur la console d'administration — cinq cents commandes
            // — c'est mesurable. `AsSplitQuery` émet une requête par niveau.
            .AsSplitQuery()
            .Where(o => o.Lines.Any(l => l.SellerId == sellerId))
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(take <= 0 ? 100 : take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// La somme des quantités vendues par ce vendeur, calculée PAR LA BASE.
    /// </summary>
    /// <remarks>
    /// `(int?)` PUIS `?? 0`, ET CE N'EST PAS UNE PRÉCAUTION DÉCORATIVE.
    ///
    /// `SUM()` rend `NULL` sur un ensemble vide, pas zéro. Sans le cast, EF traduit
    /// vers un `int` non nullable et la lecture LÈVE sur le premier vendeur qui n'a
    /// encore rien vendu — c'est-à-dire sur chaque nouveau vendeur, au moment
    /// précis de sa première commande.
    ///
    /// LE FILTRE SUR LE STATUT PORTE SUR LA COMMANDE, CELUI SUR LE VENDEUR SUR LA
    /// LIGNE. Une commande peut mêler plusieurs vendeurs : compter ses lignes sans
    /// re-filtrer donnerait à chacun les ventes des autres.
    /// </remarks>
    public async Task<int> SumSoldQuantityBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Delivered)
            .SelectMany(o => o.Lines)
            .Where(l => l.SellerId == sellerId)
            .SumAsync(l => (int?)l.Quantity, cancellationToken) ?? 0;

    public async Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
        => await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Lines)
                .ThenInclude(l => l.Options)
            // REQUÊTE ÉCLATÉE : DEUX COLLECTIONS IMBRIQUÉES SUR UNE LISTE.
            //
            // `Lines` puis `Options` en une seule requête ramène
            // commandes × lignes × options lignes SQL, que EF déduplique ensuite
            // côté client. Sur la console d'administration — cinq cents commandes
            // — c'est mesurable. `AsSplitQuery` émet une requête par niveau.
            .AsSplitQuery()
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Order> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, OrderStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Orders.AsNoTracking().AsQueryable();

        if (id is { } g)
        {
            // Les GUID sont des identifiants exacts (pas de LIKE traduisible dessus) :
            // on rapproche d'une commande OU d'un acheteur.
            var orderId = new OrderId(g);
            baseQuery = baseQuery.Where(o => o.Id == orderId || o.BuyerId == g);
        }

        var statusCounts = await baseQuery
            .GroupBy(o => o.Status)
            .Select(gr => new { Status = gr.Key, Count = gr.Count() })
            .ToListAsync(cancellationToken);

        var filtered = status is { } s ? baseQuery.Where(o => o.Status == s) : baseQuery;

        var total = await filtered.CountAsync(cancellationToken);

        // Même raison que ci-dessus : deux niveaux de collection sur une page.
        var q = filtered.Include(o => o.Lines).ThenInclude(l => l.Options).AsSplitQuery();
        IOrderedQueryable<Order> ordered = sort switch
        {
            "total" => desc ? q.OrderByDescending(o => o.GrandTotal) : q.OrderBy(o => o.GrandTotal),
            "status" => desc ? q.OrderByDescending(o => o.Status) : q.OrderBy(o => o.Status),
            _ => desc ? q.OrderByDescending(o => o.CreatedAtUtc) : q.OrderBy(o => o.CreatedAtUtc),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total, statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count));
    }
}
