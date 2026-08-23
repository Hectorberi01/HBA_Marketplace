using System.Text.Json;
using HBA.Financial.Payments.Application.Abstractions.Gateways;

namespace HBA.Financial.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Base des adaptateurs PSP qui font de vrais appels réseau. Contrairement à
/// <see cref="SimulatedPaymentGateway"/> (qui ne touche pas le réseau), cette base
/// résout un <see cref="HttpClient"/> nommé via <see cref="IHttpClientFactory"/>
/// (durée de vie des handlers gérée par le framework — pas de socket exhaustion)
/// et mutualise le parsing/vérification des webhooks. Chaque PSP implémente ses
/// appels HTTP (token, charge, statut, remboursement).
/// </summary>
public abstract class HttpPaymentGatewayBase : IPaymentGateway
{
    private readonly IHttpClientFactory _httpClientFactory;

    protected HttpPaymentGatewayBase(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public abstract string Provider { get; }

    public virtual bool RequiresPayerPhone => false;

    /// <summary>
    /// Vrai par défaut : un adaptateur HTTP est censé savoir rembourser. Les quatre
    /// qui ne le savent PAS (FedaPay, MTN MoMo, Moov, PayPal) l'annoncent en
    /// redéfinissant cette propriété — voir <see cref="IPaymentGateway.SupportsRefund"/>.
    /// </summary>
    public virtual bool SupportsRefund => true;

    /// <summary>Nom du client HTTP enregistré (AddHttpClient) pour ce PSP.</summary>
    protected abstract string HttpClientName { get; }

    /// <summary>
    /// Secret de signature des webhooks. Vide, le webhook est REJETÉ — sauf si
    /// <see cref="GatewayWebhook.AllowUnsignedWhenSecretMissing"/> a été posé
    /// explicitement au démarrage.
    /// </summary>
    protected abstract string WebhookSecret { get; }

    /// <summary>Champ du payload portant le type/statut d'événement.</summary>
    protected abstract string EventTypeField { get; }

    protected HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    public abstract Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    public abstract Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    public abstract Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    public abstract Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default);

    public virtual Task<GatewayRefundResult> RefundAsync(GatewayRefundContext context, CancellationToken cancellationToken = default)
        => RefundAsync(context.ProviderReference, cancellationToken);

    public virtual Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default)
        => Task.FromResult(GatewayWebhook.Parse(rawBody, signatureHeader, WebhookSecret, EventTypeField, MapOutcome, ExtractReference));

    /// <summary>Mappe un type/statut d'événement PSP vers un résultat normalisé.</summary>
    protected abstract GatewayOutcome MapOutcome(string eventType);

    /// <summary>Extrait l'identifiant de corrélation du payload de webhook.</summary>
    protected abstract string? ExtractReference(JsonElement root);
}
