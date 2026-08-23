using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Domain.Policies;

public static class ReturnEligibilityPolicy
{
    public static Result Evaluate(
        PolicySnapshot policy,
        ReturnResolution resolution,
        DateTime deliveredAtUtc,
        DateTime nowUtc)
    {
        if (!policy.AllowReturn && resolution != ReturnResolution.RefundOnly)
        {
            return Result.Failure(Error.BusinessRule("return.not_eligible", "La politique applicable interdit les retours physiques."));
        }

        if (resolution == ReturnResolution.RefundOnly && !policy.AllowRefundOnly)
        {
            return Result.Failure(Error.BusinessRule("return.refund_only_forbidden", "La politique applicable interdit le remboursement sans retour."));
        }

        if (nowUtc > deliveredAtUtc.AddDays(policy.ReturnWindowDays))
        {
            return Result.Failure(Error.BusinessRule("return.window_expired", "La fenetre de retour est expiree."));
        }

        return Result.Success();
    }
}
