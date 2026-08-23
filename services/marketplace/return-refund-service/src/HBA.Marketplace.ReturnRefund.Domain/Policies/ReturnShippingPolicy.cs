using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;

namespace HBA.Marketplace.ReturnRefund.Domain.Policies;

public static class ReturnShippingPolicy
{
    public static string PayerFor(ReturnReasonCode reasonCode, PolicySnapshot snapshot)
        => snapshot.CustomerPaysReturnShippingFor.Contains(reasonCode) ? "CUSTOMER" : "SELLER";
}
