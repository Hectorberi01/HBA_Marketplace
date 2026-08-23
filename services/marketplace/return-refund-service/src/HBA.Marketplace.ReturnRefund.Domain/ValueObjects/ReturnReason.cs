using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

public sealed record ReturnReason(ReturnReasonCode Code, string Category)
{
    public bool RequiresEvidence()
        => Code is ReturnReasonCode.Defective
            or ReturnReasonCode.DamagedOnArrival
            or ReturnReasonCode.NotAsDescribed
            or ReturnReasonCode.MissingParts;
}
