using HBA.Inventory.Domain.Stock;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Inventory.Application.Stock.Queries;

/// <summary>Une ligne du journal, telle qu'un vendeur la lit.</summary>
public sealed record StockMovementView(
    Guid Id,
    Guid InventoryItemId,
    string Sku,
    Guid LocationId,
    string Kind,
    int Delta,
    int OnHandAfter,
    Guid? ActorUserId,
    string? Reason,
    string? Reference,
    DateTime OccurredOnUtc);

/// <summary>
/// L'historique des mouvements d'un article.
/// </summary>
/// <remarks>
/// LA BORNE EST PLAFONNÉE ICI, PAS SEULEMENT PAR DÉFAUT. Un `take` venu du
/// client et repris tel quel rendrait la borne décorative : il suffirait de
/// demander 100 000 lignes. Le défaut sert le cas courant, le plafond ferme la
/// porte.
/// </remarks>
public sealed record ListStockMovementsQuery(Guid InventoryItemId, int Take = 50)
    : IQuery<IReadOnlyList<StockMovementView>>;

internal sealed class ListStockMovementsQueryHandler
    : IQueryHandler<ListStockMovementsQuery, IReadOnlyList<StockMovementView>>
{
    private const int PlafondDeLecture = 200;

    private readonly IStockMovementRepository _movements;

    public ListStockMovementsQueryHandler(IStockMovementRepository movements)
        => _movements = movements;

    public async Task<Result<IReadOnlyList<StockMovementView>>> Handle(
        ListStockMovementsQuery query, CancellationToken cancellationToken)
    {
        var borne = query.Take <= 0 ? 50 : Math.Min(query.Take, PlafondDeLecture);

        var mouvements = await _movements.ListByItemAsync(
            query.InventoryItemId, borne, cancellationToken);

        return Result.Success<IReadOnlyList<StockMovementView>>(
            mouvements
                .Select(m => new StockMovementView(
                    m.Id, m.InventoryItemId, m.Sku, m.LocationId, m.Kind.ToString(),
                    m.Delta, m.OnHandAfter, m.ActorUserId, m.Reason, m.Reference, m.OccurredOnUtc))
                .ToList());
    }
}
