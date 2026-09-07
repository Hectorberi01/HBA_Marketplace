namespace HBA.Commerce.Contracts;

/// <summary>API in-process publique du module Cart.</summary>
public interface ICartModuleApi
{
    Task<CartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default);
}
