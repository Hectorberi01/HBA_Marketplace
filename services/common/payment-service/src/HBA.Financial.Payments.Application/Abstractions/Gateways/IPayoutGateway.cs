namespace HBA.Financial.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Bénéficiaire d'un reversement (vendeur) : nom + numéro Mobile Money + opérateur.
/// </summary>
public sealed record PayoutBeneficiary(string Name, string Msisdn, string Provider);

/// <summary>Instruction de reversement (dépôt) vers un bénéficiaire.</summary>
public sealed record PayoutInstruction(decimal Amount, string Currency, PayoutBeneficiary Beneficiary, string Reference);

/// <summary>Issue d'une demande de reversement.</summary>
public enum PayoutOutcomeKind
{
    Accepted = 0,
    Failed = 1,
    Unknown = 2
}

/// <summary>Résultat d'une demande de reversement.</summary>
public sealed record PayoutResult(PayoutOutcomeKind Kind, string? ProviderReference, string? Error)
{
    public static PayoutResult Accepted(string providerReference) => new(PayoutOutcomeKind.Accepted, providerReference, null);
    public static PayoutResult Failed(string error) => new(PayoutOutcomeKind.Failed, null, error);
    public static PayoutResult Unknown(string error, string? providerReference = null) => new(PayoutOutcomeKind.Unknown, providerReference, error);
}

/// <summary>
/// Cycle de vie d'un dépôt chez le PSP (FedaPay : pending → started → processing →
/// sent | failed).
/// </summary>
public enum PayoutStatus
{
    Pending = 0,
    Started = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4,
    Unknown = 5
}

/// <summary>Statut courant d'un dépôt, interrogé auprès du PSP.</summary>
public sealed record PayoutStatusResult(PayoutStatus Status, string? Error);

/// <summary>Événement de webhook concernant un DÉPÔT (payout), normalisé.</summary>
public sealed record PayoutWebhookEvent(bool IsPayoutEvent, bool Verified, string? ProviderReference, PayoutStatus Status)
{
    /// <summary>Événement qui ne concerne pas un dépôt : à laisser au flux paiement.</summary>
    public static readonly PayoutWebhookEvent NotPayout = new(false, false, null, PayoutStatus.Unknown);

    /// <summary>Événement de dépôt dont la signature est invalide : à REJETER (401).</summary>
    public static readonly PayoutWebhookEvent Unsigned = new(true, false, null, PayoutStatus.Unknown);
}

/// <summary>
/// Port d'un prestataire de DÉPÔT (payout / disbursement) — distinct de
/// <see cref="IPaymentGateway"/> qui encaisse depuis l'acheteur.
/// </summary>
public interface IPayoutGateway
{
    /// <summary>Demande un reversement au bénéficiaire (Mobile Money).</summary>
    Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interroge le PSP sur le statut réel d'un dépôt déjà créé (réconciliation).
    /// </summary>
    Task<PayoutStatusResult> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconnaît et vérifie un webhook de DÉPÔT. Renvoie
    /// <see cref="PayoutWebhookEvent.NotPayout"/> si le payload concerne autre
    /// chose (paiement) : l'appelant le laissera alors au flux paiement.
    /// </summary>
    PayoutWebhookEvent ParseWebhook(string rawBody, string? signatureHeader);
}
