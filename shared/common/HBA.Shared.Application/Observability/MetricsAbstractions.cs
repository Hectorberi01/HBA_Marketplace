namespace HBA.Shared.Application.Observability;

/// <summary>
/// Abstractions de métriques métier, exposées ici (BuildingBlocks) pour que les
/// modules puissent les émettre sans dépendre de l'hôte de composition (le projet
/// <c> *.Api</c> de chaque service), où vivent les implémentations concrètes basées
/// sur <see cref="System.Diagnostics.Metrics.Meter"/> .
/// </summary>
public interface IPaymentMetrics
{
    void Attempt(string provider, string paymentMethod, string currency);
    void Success(string provider, string paymentMethod, string currency, long amountMinorUnits, double? durationSeconds = null);
    void Failed(string provider, string paymentMethod, string currency, string failureReason);
    void Pending(string provider, string paymentMethod, string currency);
    void Cancelled(string provider, string paymentMethod, string currency);
    void Refund(string provider, string currency, long amountMinorUnits);
    void WebhookError(string provider, string failureReason);
    void WebhookProcessed(string provider, double durationSeconds);
}

/// <summary>Métriques métier de la marketplace (inscriptions, commandes, CA…).</summary>
public interface IHbaBusinessMetrics
{
    void UserRegistered();
    void ProductCreated();
    void CartCreated();
    void CartAbandoned();
    void OrderCreated();
    void OrderCancelled();
    void OrderCompleted(string currency, long revenueMinorUnits = 0, long commissionMinorUnits = 0);
    void SetGauge(string name, long value);
}

/// <summary>Métriques d'authentification &amp; sécurité.</summary>
public interface ISecurityMetrics
{
    void LoginSuccess(string authenticationMethod, string clientType);
    void LoginFailed(string authenticationMethod, string failureReason, string clientType);
    void Registration(string clientType);
    void PasswordReset();
    void AccountLocked(string failureReason);
    void TokenValidationFailed(string failureReason);
    void Unauthorized(string route);
    void Forbidden(string route);
    void RateLimited(string route);
}

/// <summary>Santé de l'outbox.</summary>
public interface IOutboxMetrics
{
    /// <summary>Une tentative de publication a échoué (sera réessayée).</summary>
    void PublishFailed(string module, string eventType);

    /// <summary>Un message a épuisé ses tentatives et ne sera PLUS JAMAIS traité.</summary>
    void DeadLettered(string module, string eventType);
}
