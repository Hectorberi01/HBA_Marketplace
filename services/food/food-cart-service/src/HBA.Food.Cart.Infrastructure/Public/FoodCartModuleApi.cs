using HBA.FoodCarts.Application.Carts.Queries;
using HBA.FoodCarts.Contracts;
using MediatR;

namespace HBA.FoodCarts.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du panier de restauration.
/// Délègue aux requêtes de l'Application ; rend null si le panier est absent.
/// </summary>
internal sealed class FoodCartModuleApi : IFoodCartModuleApi
{
    private readonly ISender _sender;

    public FoodCartModuleApi(ISender sender) => _sender = sender;

    public async Task<FoodCartSummary?> GetActiveCartAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
    {
        var resultat = await _sender.Send(new GetActiveFoodCartQuery(buyerId), cancellationToken);
        return resultat.IsSuccess ? resultat.Value : null;
    }

    public async Task<FoodCartSummary?> GetCartAsync(
        Guid cartId, CancellationToken cancellationToken = default)
    {
        var resultat = await _sender.Send(new GetFoodCartByIdQuery(cartId), cancellationToken);
        return resultat.IsSuccess ? resultat.Value : null;
    }
}
