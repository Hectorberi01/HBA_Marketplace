using HBA.FoodOrders.Application.Orders.Queries;
using HBA.FoodOrders.Contracts;
using HBA.FoodOrders.Domain.Orders;
using MediatR;

namespace HBA.FoodOrders.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique des commandes de repas.
/// </summary>
internal sealed class MealOrderModuleApi : IMealOrderModuleApi
{
    private readonly ISender _sender;
    private readonly IMealOrderRepository _orders;

    public MealOrderModuleApi(ISender sender, IMealOrderRepository orders)
    {
        _sender = sender;
        _orders = orders;
    }

    public async Task<MealOrderSummary?> GetOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var resultat = await _sender.Send(new GetMealOrderQuery(orderId), cancellationToken);
        return resultat.IsSuccess ? resultat.Value : null;
    }

    /// <summary>
    /// LECTURE DIRECTE AU DÉPÔT, SANS PASSER PAR MEDIATR.
    ///
    /// C'est un simple EXISTS sur index, appelé à CHAQUE valorisation de panier.
    /// Y interposer un pipeline de requête — validation, journalisation,
    /// transaction — coûterait plus que la requête elle-même.
    /// </summary>
    public Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => _orders.HasPurchasedAsync(buyerId, cancellationToken);
}
