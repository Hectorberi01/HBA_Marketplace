using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Repositories;

internal sealed class ReturnPolicyRepository : IReturnPolicyRepository
{
    public Task<PolicySnapshot> GetApplicableSnapshotAsync(Guid productId, Guid categoryId, CancellationToken cancellationToken)
        => Task.FromResult(new PolicySnapshot(
            "default",
            "2026.08.1",
            14,
            AllowReturn: true,
            AllowRefundOnly: true,
            RequireEvidence: true,
            RequireInspection: true,
            RestockingFeePercent: 0m,
            CustomerPaysReturnShippingFor: [ReturnReasonCode.ChangedMind],
            AutoApproveReasons: [ReturnReasonCode.WrongItem, ReturnReasonCode.DamagedOnArrival]));
}
