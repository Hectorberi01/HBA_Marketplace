namespace HBA.Marketplace.ReturnRefund.Domain.Exceptions;

public sealed class ReturnRefundDomainException(string message) : InvalidOperationException(message);
