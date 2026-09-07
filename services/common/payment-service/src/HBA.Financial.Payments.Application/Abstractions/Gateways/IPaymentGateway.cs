namespace HBA.Financial.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Contexte d'une demande de paiement transmis au PSP. <see cref="PayerMsisdn"/>
/// (numéro du payeur) n'est renseigné que pour le Mobile Money (RequestToPay).
/// </summary>
public sealed record GatewayChargeContext(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string? ReturnUrl,
    string? CancelUrl,
    string? PayerMsisdn = null);

/// <summary>
/// Session créée côté PSP. Selon le flux : - HostedCheckout :
/// <see cref="RedirectUrl"/> renseigné (rediriger l'acheteur).
/// </summary>
public sealed record GatewaySession(string ProviderReference, string? RedirectUrl, string? ClientSecret);

/// <summary>Résultat normalisé d'un événement PSP (webhook ou interrogation de statut).</summary>
public enum GatewayOutcome
{
    Pending = 0,
    Captured = 1,
    Failed = 2,
    Refunded = 3,
    Ignored = 4
}

/// <summary>Événement PSP normalisé, indépendant du fournisseur.</summary>
/// <param name="RefundAmount">
/// Montant de CE remboursement, en unités majeures (2 500 F = 2500m).
/// </param>
/// <param name="TotalRefundedAmount">
/// CUMUL remboursé sur la transaction chez le prestataire, en unités majeures.
/// </param>
/// <param name="RefundCurrency">Devise annoncée par le prestataire pour le remboursement.</param>
/// <param name="RefundReference">
/// Identifiant du remboursement CHEZ LE PRESTATAIRE. Sert de clé d'idempotence :
/// deux livraisons du même webhook produisent la même clé, donc une seule ligne
/// dans `payment_refunds` (index unique posé au lot 3.1).
/// </param>
public sealed record GatewayEvent(
    bool Verified,
    GatewayOutcome Outcome,
    string? ProviderReference,
    string? FailureReason,
    decimal? RefundAmount = null,
    decimal? TotalRefundedAmount = null,
    string? RefundCurrency = null,
    string? RefundReference = null);

/// <summary>Résultat d'un remboursement demandé au PSP.</summary>
/// <param name="Transient">
/// Vrai si l'échec vient du transport et non d'une décision du prestataire.
/// </param>
public sealed record GatewayRefundResult(bool Success, string? ProviderReference, string? Error, bool Transient = false);

/// <summary>Contexte complet d'un remboursement, avec idempotence inter-service.</summary>
public sealed record GatewayRefundContext(
    string ProviderReference,
    decimal Amount,
    string Currency,
    string Reason,
    string IdempotencyKey);

/// <summary>Port (hexagonal) d'un prestataire de paiement.</summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Nom du prestataire, ex. « Stripe », « PayPal », « MtnMomo », « Moov »
    /// (insensible à la casse).
    /// </summary>
    string Provider { get; }

    /// <summary>Vrai si le PSP exige le numéro du payeur (Mobile Money : RequestToPay).</summary>
    bool RequiresPayerPhone { get; }

    /// <summary>
    /// Vrai si cet adaptateur sait RÉELLEMENT demander un remboursement au
    /// prestataire.
    /// </summary>
    bool SupportsRefund => true;

    /// <summary>Crée une page de paiement hébergée et renvoie l'URL de redirection.</summary>
    Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée une intention de paiement à confirmer côté client (renvoie un client
    /// secret).
    /// </summary>
    Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    /// <summary>Vérifie la signature et normalise le payload d'un webhook PSP.</summary>
    Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interroge le statut courant d'une session/intention (retour de redirection,
    /// réconciliation).
    /// </summary>
    Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Demande le remboursement d'un paiement encaissé.</summary>
    Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Demande le remboursement d'un paiement encaissé avec montant et clé
    /// d'idempotence.
    /// </summary>
    Task<GatewayRefundResult> RefundAsync(GatewayRefundContext context, CancellationToken cancellationToken = default)
        => RefundAsync(context.ProviderReference, cancellationToken);
}
