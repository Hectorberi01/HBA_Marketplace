using HBA.Gateway.Application.Contracts.Order;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>order-service</c>.</summary>
public interface IOrderClient : IServiceClient
{
    /// <summary><c>GET /api/orders/</c> — les commandes de l'appelant.</summary>
    Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListMineAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/sellers/{sellerId}/orders</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken);
}
