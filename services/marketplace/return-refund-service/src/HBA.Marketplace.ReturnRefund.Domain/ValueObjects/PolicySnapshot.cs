using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

public sealed record PolicySnapshot(
    string PolicyId,
    string Version,
    int ReturnWindowDays,
    bool AllowReturn,
    bool AllowRefundOnly,
    bool RequireEvidence,
    bool RequireInspection,
    decimal RestockingFeePercent,
    IReadOnlyCollection<ReturnReasonCode> CustomerPaysReturnShippingFor,
    IReadOnlyCollection<ReturnReasonCode> AutoApproveReasons);
