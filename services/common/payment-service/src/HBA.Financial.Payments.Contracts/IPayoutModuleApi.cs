namespace HBA.Financial.Payments.Contracts;

/// <summary>
/// Instruction de reversement Mobile Money exposée aux autres modules (Settlement).
/// </summary>
public sealed record PayoutInstructionContract(
    decimal Amount,
    string Currency,
    string BeneficiaryName,
    string Msisdn,
    string Provider,
    string Reference);

/// <summary>
/// Issue d'une demande de reversement : Accepted = demandé au PSP (PAS encore reçu
/// par le vendeur) ; Failed = rejet définitif (remboursement sûr) ; Unknown =
/// indéterminé — NE JAMAIS rembourser, l'argent est peut-être parti (rembourser
/// autoriserait une seconde validation, donc un double versement).
/// </summary>
public enum PayoutOutcomeStatus
{
    Accepted = 0,
    Failed = 1,
    Unknown = 2
}

/// <summary>Résultat d'une demande de reversement.</summary>
public sealed record PayoutOutcome(PayoutOutcomeStatus Status, string? ProviderReference, string? Error);

/// <summary>
/// Statut réel d'un dépôt chez le PSP. Seul <see cref="Sent"/> prouve le versement.
/// </summary>
public enum PayoutProgress
{
    Pending = 0,
    Started = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4,
    Unknown = 5
}

/// <summary>
/// Notification de webhook concernant un DÉPÔT, normalisée pour les autres modules.
/// </summary>
public sealed record PayoutWebhookNotification(bool IsPayoutEvent, bool Verified, string? ProviderReference, PayoutProgress Progress);

/// <summary>API publique de reversement du module Payments.</summary>
public interface IPayoutModuleApi
{
    Task<PayoutOutcome> SendMobileMoneyPayoutAsync(PayoutInstructionContract instruction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Statut réel d'un dépôt déjà demandé (réconciliation des retraits « en cours
    /// »).
    /// </summary>
    Task<PayoutProgress> GetPayoutProgressAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Reconnaît et vérifie un webhook de dépôt (signature comprise).</summary>
    PayoutWebhookNotification ReadPayoutWebhook(string rawBody, string? signatureHeader);
}
