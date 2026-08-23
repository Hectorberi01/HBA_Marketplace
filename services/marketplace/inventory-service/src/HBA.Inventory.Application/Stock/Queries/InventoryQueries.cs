using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Contracts;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock.Queries;

/// <summary>Récupère un article de stock par son identifiant.</summary>
public sealed record GetInventoryItemQuery(Guid InventoryItemId) : IQuery<InventoryItemSummary>;

/// <summary>Disponibilité agrégée d'un SKU (toutes localisations).</summary>
public sealed record GetAvailabilityQuery(string Sku) : IQuery<AvailabilitySummary>;

/// <summary>Liste les articles sous le seuil de réapprovisionnement.</summary>
/// <param name="Take">
/// Combien de lignes au plus. Plafonné côté serveur — voir
/// <c>ListLowStockQueryHandler.PlafondDeLecture</c>.
/// </param>
public sealed record ListLowStockQuery(int Take = 50) : IQuery<IReadOnlyList<InventoryItemSummary>>;

/// <summary>Liste les articles de stock d'un SKU (toutes localisations).</summary>
public sealed record ListInventoryBySkuQuery(string Sku) : IQuery<IReadOnlyList<InventoryItemSummary>>;

/// <summary>
/// Liste les articles de stock situés dans un ensemble de localisations.
///
/// Sert au BFF Vendeur : ce module ignorant la notion de boutique, l'appelant résout
/// d'abord les localisations du vendeur, puis interroge cette requête. C'est ce qui
/// permet d'afficher l'inventaire complet d'une boutique en une requête, au lieu d'un
/// appel `ListInventoryBySkuQuery` par référence du catalogue.
/// </summary>
public sealed record ListInventoryByLocationsQuery(IReadOnlyCollection<Guid> LocationIds)
    : IQuery<IReadOnlyList<InventoryItemSummary>>;

internal sealed class ListInventoryByLocationsQueryHandler
    : IQueryHandler<ListInventoryByLocationsQuery, IReadOnlyList<InventoryItemSummary>>
{
    private readonly IInventoryItemRepository _repository;

    public ListInventoryByLocationsQueryHandler(IInventoryItemRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<InventoryItemSummary>>> Handle(
        ListInventoryByLocationsQuery query, CancellationToken cancellationToken)
    {
        var items = await _repository.ListByLocationsAsync(query.LocationIds, cancellationToken);
        IReadOnlyList<InventoryItemSummary> summaries = items.Select(InventoryMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListInventoryBySkuQueryHandler : IQueryHandler<ListInventoryBySkuQuery, IReadOnlyList<InventoryItemSummary>>
{
    private readonly IInventoryItemRepository _repository;

    public ListInventoryBySkuQueryHandler(IInventoryItemRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<InventoryItemSummary>>> Handle(ListInventoryBySkuQuery query, CancellationToken cancellationToken)
    {
        var skuResult = Sku.Create(query.Sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<InventoryItemSummary>>(skuResult.Error);
        }

        var items = await _repository.ListBySkuAsync(skuResult.Value.Value, cancellationToken);
        IReadOnlyList<InventoryItemSummary> summaries = items.Select(InventoryMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class GetInventoryItemQueryHandler : IQueryHandler<GetInventoryItemQuery, InventoryItemSummary>
{
    private readonly IInventoryItemRepository _repository;

    public GetInventoryItemQueryHandler(IInventoryItemRepository repository) => _repository = repository;

    public async Task<Result<InventoryItemSummary>> Handle(GetInventoryItemQuery query, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(new InventoryItemId(query.InventoryItemId), cancellationToken);
        return item is null
            ? Error.NotFound("inventory.item.not_found", "Article de stock introuvable.")
            : InventoryMapper.ToSummary(item);
    }
}

internal sealed class GetAvailabilityQueryHandler : IQueryHandler<GetAvailabilityQuery, AvailabilitySummary>
{
    private readonly IInventoryItemRepository _repository;

    public GetAvailabilityQueryHandler(IInventoryItemRepository repository) => _repository = repository;

    public async Task<Result<AvailabilitySummary>> Handle(GetAvailabilityQuery query, CancellationToken cancellationToken)
    {
        var skuResult = Sku.Create(query.Sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure<AvailabilitySummary>(skuResult.Error);
        }

        var items = await _repository.ListBySkuAsync(skuResult.Value.Value, cancellationToken);
        var total = items.Sum(i => i.Available);
        return new AvailabilitySummary(skuResult.Value.Value, total);
    }
}

internal sealed class ListLowStockQueryHandler : IQueryHandler<ListLowStockQuery, IReadOnlyList<InventoryItemSummary>>
{
    /// <summary>
    /// Le plafond serveur, identique à celui de <c>ListStockMovementsQueryHandler</c>.
    /// </summary>
    /// <remarks>
    /// C'EST UNE ALERTE, PAS UN INVENTAIRE. Deux cents lignes sous seuil, c'est
    /// déjà plus que ce qu'un gestionnaire traite dans sa journée. Le plafond est
    /// posé ICI, dans l'application, et non laissé au client : un `take` venu de la
    /// requête ne doit jamais pouvoir rouvrir le balayage complet que ce lot ferme.
    /// </remarks>
    private const int PlafondDeLecture = 200;

    private readonly IInventoryItemRepository _repository;

    public ListLowStockQueryHandler(IInventoryItemRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<InventoryItemSummary>>> Handle(ListLowStockQuery query, CancellationToken cancellationToken)
    {
        var borne = query.Take <= 0 ? 50 : Math.Min(query.Take, PlafondDeLecture);

        var items = await _repository.ListLowStockAsync(borne, cancellationToken);
        IReadOnlyList<InventoryItemSummary> summaries = items.Select(InventoryMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
