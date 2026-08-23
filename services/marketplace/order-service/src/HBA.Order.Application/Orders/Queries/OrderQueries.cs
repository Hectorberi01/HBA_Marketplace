using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Orders.Contracts;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Application.Orders.Queries;

/// <summary>
/// Récupère une commande par son identifiant.
/// </summary>
/// <param name="RequesterId">
/// ═════════════════════════════════════════════════════════════════════════════
/// NULL SIGNIFIE « LE SYSTÈME », PAS « N'IMPORTE QUI ».
///
/// La route HTTP n'exigeait qu'un jeton et ne regardait pas l'acheteur : avec un
/// identifiant de commande — glané dans un ticket de support, une capture
/// d'écran, ou tiré au sort — n'importe quel inscrit obtenait l'adresse de
/// livraison, le téléphone et les montants d'un tiers.
///
/// Le contrôle ne peut pas vivre dans la route : `IOrderModuleApi` sert la même
/// requête à travers gRPC, pour des appels de service à service qui n'ont pas
/// d'utilisateur. D'où ce paramètre optionnel — null pour un appelant interne,
/// déjà filtré par la clé partagée ; renseigné pour un humain, et alors vérifié.
///
/// Un appelant qui n'est pas l'acheteur reçoit « introuvable », pas « interdit » :
/// répondre 403 confirmerait que la commande existe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </param>
public sealed record GetOrderQuery(Guid OrderId, Guid? RequesterId = null) : IQuery<OrderSummary>;

/// <summary>Liste les commandes d'un acheteur.</summary>
/// <param name="Take">Combien de commandes au plus. Plafonné côté serveur.</param>
public sealed record ListMyOrdersQuery(Guid BuyerId, int Take = 50)
    : IQuery<IReadOnlyList<OrderSummary>>;

/// <summary>Liste les commandes comportant au moins une ligne d'un vendeur (back-office vendeur).</summary>
/// <param name="Take">Combien de commandes au plus. Plafonné côté serveur.</param>
public sealed record ListOrdersBySellerQuery(Guid SellerId, int Take = 50)
    : IQuery<IReadOnlyList<OrderSummary>>;

/// <summary>Page de commandes pour la console admin (filtre statut, recherche par identifiant).</summary>
public sealed record ListAllOrdersQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Sort = null,
    string? Dir = null) : IQuery<PagedResult<OrderSummary>>;

internal sealed class ListAllOrdersQueryHandler : IQueryHandler<ListAllOrdersQuery, PagedResult<OrderSummary>>
{
    private readonly IOrderRepository _repository;

    public ListAllOrdersQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<PagedResult<OrderSummary>>> Handle(ListAllOrdersQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        OrderStatus? status = Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var parsed) ? parsed : null;
        Guid? id = Guid.TryParse(query.Search, out var g) ? g : null;
        bool desc = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        var (orders, total, statusCounts) = await _repository.ListPagedAsync(page, pageSize, id, status, query.Sort, desc, cancellationToken);
        var items = orders.Select(OrderMapper.ToSummary).ToList();
        return Result.Success(new PagedResult<OrderSummary>(items, total, page, pageSize, statusCounts));
    }
}

internal sealed class ListOrdersBySellerQueryHandler : IQueryHandler<ListOrdersBySellerQuery, IReadOnlyList<OrderSummary>>
{
    private const int PlafondDeLecture = 200;

    private readonly IOrderRepository _repository;
    private readonly ISellerOrderRepository _sellerOrders;

    public ListOrdersBySellerQueryHandler(IOrderRepository repository, ISellerOrderRepository sellerOrders)
    {
        _repository = repository;
        _sellerOrders = sellerOrders;
    }

    public async Task<Result<IReadOnlyList<OrderSummary>>> Handle(ListOrdersBySellerQuery query, CancellationToken cancellationToken)
    {
        // LA MÊME BORNE POUR LES DEUX LECTURES, ET C'EST OBLIGATOIRE.
        //
        // Elles sont jointes par `OrderId` juste en dessous. Deux bornes
        // différentes feraient perdre son état vendeur aux commandes remontées par
        // l'une et pas par l'autre — silencieusement, et seulement sur les plus
        // anciennes. Un défaut invisible en test avec peu de données.
        var borne = query.Take <= 0 ? 50 : Math.Min(query.Take, PlafondDeLecture);

        var orders = await _repository.ListBySellerAsync(query.SellerId, borne, cancellationToken);

        // ══════════════════════════════════════════════════════════════════════
        // DEUX LECTURES, PAS UNE JOINTURE — ET PAS UNE PAR COMMANDE.
        //
        // `SellerOrder` est un agrégat distinct, avec son propre verrou : le
        // charger sous la commande ferait salir `orders` à chaque geste d'un
        // vendeur, donc mettrait deux vendeurs d'une même commande en
        // concurrence sur la MÊME ligne parente (voir
        // `ISellerOrderRepository`). Et une lecture par commande dans la boucle
        // en dessous ferait cinquante requêtes pour un carnet de cinquante
        // commandes — le N+1 classique, sur l'écran de travail du vendeur.
        //
        // Une lecture de tout son carnet, indexée par commande, suffit.
        // ══════════════════════════════════════════════════════════════════════
        var parts = (await _sellerOrders.ListBySellerAsync(query.SellerId, borne, cancellationToken))
            .ToDictionary(p => p.OrderId);

        // `ToSellerSummary`, ET SURTOUT PAS `ToSummary`.
        //
        // La route est bien gardée — appartenance vérifiée, `ORDER_VIEW` exigée —
        // mais elle rendait la commande ENTIÈRE : les lignes des concurrents, les
        // totaux de toute la commande, et les coordonnées GPS et le téléphone de
        // l'acheteur. L'autorisation disait « ce carnet est le vôtre » ; la
        // projection montrait celui de tout le monde.
        IReadOnlyList<OrderSummary> summaries = orders
            .Select(o => OrderMapper.ToSellerSummary(
                o,
                query.SellerId,

                // Nulle pour un repas, et nulle pour toute commande confirmée
                // AVANT la migration `CommandeParVendeur` : le carnet reste
                // lisible, sans état vendeur, exactement comme avant.
                parts.TryGetValue(o.Id.Value, out var part) ? part : null))
            .ToList();

        return Result.Success(summaries);
    }
}

internal sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderSummary>
{
    private readonly IOrderRepository _repository;

    public GetOrderQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<OrderSummary>> Handle(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var order = await _repository.GetByIdAsync(new OrderId(query.OrderId), cancellationToken);

        // Introuvable, ou appartenant à quelqu'un d'autre : même réponse. La
        // distinction serait déjà une information.
        if (order is null || (query.RequesterId is { } requesterId && order.BuyerId != requesterId))
        {
            return Error.NotFound("ordering.not_found", "Commande introuvable.");
        }

        return OrderMapper.ToSummary(order);
    }
}

internal sealed class ListMyOrdersQueryHandler : IQueryHandler<ListMyOrdersQuery, IReadOnlyList<OrderSummary>>
{
    /// <summary>
    /// Un `Take` venu du client ne doit pas pouvoir rouvrir le chargement
    /// intégral de l'historique que ce lot ferme.
    /// </summary>
    private const int PlafondDeLecture = 200;

    private readonly IOrderRepository _repository;

    public ListMyOrdersQueryHandler(IOrderRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<OrderSummary>>> Handle(ListMyOrdersQuery query, CancellationToken cancellationToken)
    {
        var borne = query.Take <= 0 ? 50 : Math.Min(query.Take, PlafondDeLecture);

        var orders = await _repository.ListByBuyerAsync(query.BuyerId, borne, cancellationToken);
        IReadOnlyList<OrderSummary> summaries = orders.Select(OrderMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
