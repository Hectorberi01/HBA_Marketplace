using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.InventoryClient;

/// <summary>BOUCHON : la marchandise retournée n'est jamais remise en stock.</summary>
internal sealed class InventoryGrpcClient : IInventoryGrpcClient
{
    public Task<Result> ProcessReturnedStockAsync(Guid returnId, Guid orderItemId, StockDisposition disposition, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success());
}
