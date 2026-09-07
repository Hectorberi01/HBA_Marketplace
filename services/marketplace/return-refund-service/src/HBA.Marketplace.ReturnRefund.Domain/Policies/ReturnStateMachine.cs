using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Domain.Policies;

public static class ReturnStateMachine
{
    private static readonly IReadOnlyDictionary<ReturnStatus, ReturnStatus[]> Transitions =
        new Dictionary<ReturnStatus, ReturnStatus[]>
        {
            [ReturnStatus.Requested] = [ReturnStatus.EligibilityCheck, ReturnStatus.AwaitingApproval, ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.Cancelled],
            [ReturnStatus.EligibilityCheck] = [ReturnStatus.AwaitingApproval, ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.ManualReview],
            [ReturnStatus.AwaitingApproval] = [ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.Cancelled, ReturnStatus.Expired],
            [ReturnStatus.Approved] = [ReturnStatus.AwaitingReturn, ReturnStatus.RefundPending, ReturnStatus.Cancelled],
            [ReturnStatus.AwaitingReturn] = [ReturnStatus.InReturnTransit, ReturnStatus.Received, ReturnStatus.Expired],
            [ReturnStatus.InReturnTransit] = [ReturnStatus.Received, ReturnStatus.ManualReview],
            [ReturnStatus.Received] = [ReturnStatus.InspectionPending, ReturnStatus.RefundPending, ReturnStatus.RejectedAfterInspection],
            [ReturnStatus.InspectionPending] = [ReturnStatus.RefundPending, ReturnStatus.RejectedAfterInspection],
            [ReturnStatus.RefundPending] = [ReturnStatus.Refunded, ReturnStatus.ManualReview],
            [ReturnStatus.Refunded] = [ReturnStatus.Closed],
            [ReturnStatus.ManualReview] = [ReturnStatus.Approved, ReturnStatus.Rejected, ReturnStatus.RefundPending, ReturnStatus.Closed],
            [ReturnStatus.Rejected] = [ReturnStatus.Closed],
            [ReturnStatus.RejectedAfterInspection] = [ReturnStatus.Closed],
            [ReturnStatus.Cancelled] = [ReturnStatus.Closed],
            [ReturnStatus.Expired] = [ReturnStatus.Closed],
            [ReturnStatus.Closed] = []
        };

    /// <summary>Cette transition est-elle permise ?</summary>
    public static bool CanTransition(ReturnStatus from, ReturnStatus to)
        => Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
}
