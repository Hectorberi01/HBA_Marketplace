using HBA.Dispatch.Domain.Dispatching;

namespace HBA.Delivery.Dispatch.Domain.Repositories;

public interface IDispatchJobRepository
{
    Task<DispatchJob?> GetByDeliveryIdAsync(Guid deliveryId, CancellationToken cancellationToken = default);

    Task SaveAsync(DispatchJob dispatchJob, CancellationToken cancellationToken = default);
}
