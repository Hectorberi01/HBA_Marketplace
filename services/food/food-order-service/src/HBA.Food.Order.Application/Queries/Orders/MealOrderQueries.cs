using HBA.FoodOrders.Contracts;
using HBA.FoodOrders.Domain.Orders;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using OrderAggregate = HBA.FoodOrders.Domain.Orders.MealOrder;

namespace HBA.FoodOrders.Application.Orders.Queries;

/// <summary>
/// Une commande de repas.
/// </summary>
/// <param name="RequesterId">
/// L'acheteur, quand la lecture vient d'une route client. Nul pour un appel
/// interne — l'API publique du service, lue par la cuisine ou la comptabilité.
///
/// « INTROUVABLE » ET NON « INTERDIT » pour la commande d'un tiers : un 403
/// confirmerait qu'elle existe, et permettrait d'énumérer les commandes de la
/// plateforme en essayant des identifiants.
/// </param>
public sealed record GetMealOrderQuery(Guid OrderId, Guid? RequesterId = null) : IQuery<MealOrderSummary>;

/// <summary>Mes commandes de repas.</summary>
public sealed record ListMyMealOrdersQuery(Guid BuyerId) : IQuery<IReadOnlyList<MealOrderSummary>>;

/// <summary>Les commandes d'un restaurant (espace restaurateur).</summary>
public sealed record ListMealOrdersByRestaurantQuery(Guid RestaurantId)
    : IQuery<IReadOnlyList<MealOrderSummary>>;

internal sealed class GetMealOrderQueryHandler : IQueryHandler<GetMealOrderQuery, MealOrderSummary>
{
    private readonly IMealOrderRepository _orders;

    public GetMealOrderQueryHandler(IMealOrderRepository orders) => _orders = orders;

    public async Task<Result<MealOrderSummary>> Handle(
        GetMealOrderQuery query, CancellationToken cancellationToken)
    {
        var commande = await _orders.GetByIdAsync(new MealOrderId(query.OrderId), cancellationToken);

        if (commande is null
            || (query.RequesterId is { } demandeur && commande.BuyerId != demandeur))
        {
            return Error.NotFound("food_ordering.not_found", "Commande introuvable.");
        }

        return MealOrderMapper.ToSummary(commande);
    }
}

internal sealed class ListMyMealOrdersQueryHandler
    : IQueryHandler<ListMyMealOrdersQuery, IReadOnlyList<MealOrderSummary>>
{
    private readonly IMealOrderRepository _orders;

    public ListMyMealOrdersQueryHandler(IMealOrderRepository orders) => _orders = orders;

    public async Task<Result<IReadOnlyList<MealOrderSummary>>> Handle(
        ListMyMealOrdersQuery query, CancellationToken cancellationToken)
    {
        var commandes = await _orders.ListByBuyerAsync(query.BuyerId, cancellationToken: cancellationToken);
        return Result.Success<IReadOnlyList<MealOrderSummary>>(
            commandes.Select(MealOrderMapper.ToSummary).ToList());
    }
}

internal sealed class ListMealOrdersByRestaurantQueryHandler
    : IQueryHandler<ListMealOrdersByRestaurantQuery, IReadOnlyList<MealOrderSummary>>
{
    private readonly IMealOrderRepository _orders;

    public ListMealOrdersByRestaurantQueryHandler(IMealOrderRepository orders) => _orders = orders;

    public async Task<Result<IReadOnlyList<MealOrderSummary>>> Handle(
        ListMealOrdersByRestaurantQuery query, CancellationToken cancellationToken)
    {
        var commandes = await _orders.ListByRestaurantAsync(query.RestaurantId, cancellationToken: cancellationToken);
        return Result.Success<IReadOnlyList<MealOrderSummary>>(
            commandes.Select(MealOrderMapper.ToSummary).ToList());
    }
}

/// <summary>Projection du domaine vers le contrat public.</summary>
internal static class MealOrderMapper
{
    public static MealOrderSummary ToSummary(OrderAggregate commande)
        => new(
            commande.Id.Value,
            commande.BuyerId,
            commande.RestaurantId,
            commande.Status.ToString(),
            commande.Subtotal,
            commande.ShippingFee,
            commande.GrandTotal,
            commande.Currency,
            commande.PromotionCode,
            commande.DeliveryQuoteId,
            commande.CustomerNote,
            commande.CreatedAtUtc,
            commande.Lines
                .Select(l => new MealOrderLineSummary(
                    l.Id,
                    l.MenuItemId,
                    l.Name,
                    l.Quantity,
                    l.FinalUnitPrice,
                    l.LineTotal,
                    commande.Currency,
                    l.Notes,
                    l.Options
                        .Select(o => new MealOrderLineOptionSummary(o.OptionGroupId, o.OptionId))
                        .ToList()))
                .ToList(),

            // LE LIBELLÉ DE COMMUNE, PAS LE CODE — voir
            // `MealOrderShippingAddressSummary`. `ShipToCommuneName` est la
            // propriété dérivée de l'agrégat, qui passe par `BeninGeography` : une
            // seule traduction, ici, plutôt qu'une seconde chez le consommateur.
            ShippingAddress: new MealOrderShippingAddressSummary(
                commande.ShipToRecipient,
                commande.ShipToPhone,
                commande.ShipToCommuneName,
                commande.ShipToQuartier,
                commande.ShipToLandmark,
                commande.ShipToLine1,
                commande.ShipToLatitude,
                commande.ShipToLongitude));
}
