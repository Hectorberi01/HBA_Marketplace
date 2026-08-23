using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

namespace HBA.Marketplace.ReturnRefund.Domain.Repositories;

public interface IReturnPolicyRepository
{
    Task<PolicySnapshot> GetApplicableSnapshotAsync(Guid productId, Guid categoryId, CancellationToken cancellationToken);
}
