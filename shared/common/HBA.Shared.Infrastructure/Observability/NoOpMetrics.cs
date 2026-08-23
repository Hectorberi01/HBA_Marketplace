using HBA.Shared.Application.Observability;

namespace HBA.Shared.Infrastructure.Observability;

internal sealed class NoOpPaymentMetrics : IPaymentMetrics
{
    public void Attempt(string provider, string paymentMethod, string currency) { }

    public void Success(string provider, string paymentMethod, string currency, long amountMinorUnits, double? durationSeconds = null) { }

    public void Failed(string provider, string paymentMethod, string currency, string failureReason) { }

    public void Pending(string provider, string paymentMethod, string currency) { }

    public void Cancelled(string provider, string paymentMethod, string currency) { }

    public void Refund(string provider, string currency, long amountMinorUnits) { }

    public void WebhookError(string provider, string failureReason) { }

    public void WebhookProcessed(string provider, double durationSeconds) { }
}

internal sealed class NoOpBusinessMetrics : IHbaBusinessMetrics
{
    public void UserRegistered() { }

    public void ProductCreated() { }

    public void CartCreated() { }

    public void CartAbandoned() { }

    public void OrderCreated() { }

    public void OrderCancelled() { }

    public void OrderCompleted(string currency, long revenueMinorUnits = 0, long commissionMinorUnits = 0) { }

    public void SetGauge(string name, long value) { }
}

internal sealed class NoOpSecurityMetrics : ISecurityMetrics
{
    public void LoginSuccess(string authenticationMethod, string clientType) { }

    public void LoginFailed(string authenticationMethod, string failureReason, string clientType) { }

    public void Registration(string clientType) { }

    public void PasswordReset() { }

    public void AccountLocked(string failureReason) { }

    public void TokenValidationFailed(string failureReason) { }

    public void Unauthorized(string route) { }

    public void Forbidden(string route) { }

    public void RateLimited(string route) { }
}

internal sealed class NoOpOutboxMetrics : IOutboxMetrics
{
    public void PublishFailed(string module, string eventType) { }

    public void DeadLettered(string module, string eventType) { }
}
