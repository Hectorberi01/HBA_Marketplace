using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.DeliveryClient;

/// <summary>BOUCHON : aucune course de retour n'est jamais créée.</summary>
internal sealed class DeliveryGrpcClient : IDeliveryGrpcClient
{
    public Task<Result<string>> CreateReturnDeliveryAsync(Guid returnId, Guid orderId, Guid sellerId, Guid customerId, CancellationToken cancellationToken)
        => Task.FromResult<Result<string>>($"RET-DELIVERY-{returnId:N}");
}
