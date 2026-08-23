namespace HBA.Marketplace.ReturnRefund.Api.Authorization;

public static class ReturnAuthorizationPolicies
{
    public const string Create = "return:create";
    public const string ReadOwn = "return:read-own";
    public const string CancelOwn = "return:cancel-own";
    public const string ReadStore = "return:read-store";
    public const string Approve = "return:approve";
    public const string Reject = "return:reject";
    public const string Inspect = "return:inspect";
    public const string DecideRefund = "refund:decide";
    public const string Override = "return:override";
    public const string ManagePolicy = "return-policy:manage";
}
