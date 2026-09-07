namespace HBA.Payments.Contracts;

/// <summary>API in-process publique du module Payments.</summary>
public interface IPaymentsModuleApi
{
    Task<PaymentSummary?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<PaymentSummary?> GetPaymentByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
