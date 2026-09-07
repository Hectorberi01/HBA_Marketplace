using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Order;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Order;

/// <inheritdoc cref="IOrderClient" />
public sealed class OrderClient : ServiceHttpClient, IOrderClient
{
    public OrderClient(HttpClient http, ILogger<OrderClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Order;

    public Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListMineAsync(
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<OrderBrief>>("/api/orders/", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<OrderBrief>>(
            $"/api/sellers/{sellerId}/orders", cancellationToken);
}
