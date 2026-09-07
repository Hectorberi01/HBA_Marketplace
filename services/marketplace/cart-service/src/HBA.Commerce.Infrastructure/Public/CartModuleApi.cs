using MediatR;
using HBA.Commerce.Application.Carts.Queries;
using HBA.Commerce.Contracts;

namespace HBA.Commerce.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Cart.</summary>
internal sealed class CartModuleApi : ICartModuleApi
{
    private readonly ISender _sender;

    public CartModuleApi(ISender sender) => _sender = sender;

    public async Task<CartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetActiveCartQuery(buyerId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCartByIdQuery(cartId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
