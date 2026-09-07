using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Contracts;

/// <summary>Soldes du portefeuille d'un vendeur.</summary>
public sealed record SellerWalletView(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    // Somme des retraits EN COURS DE ROUTE : demandés (attente de validation admin)
    // ET en cours de versement chez le PSP. Dans les deux cas les fonds sont déjà
    // retenus, retirés du solde principal.
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Soldes du portefeuille d'un livreur.</summary>
public sealed record DriverWalletView(
    Guid DriverId,
    decimal AvailableBalance,

    // Total gagné depuis l'inscription, retraits compris.
    decimal LifetimeEarned,
    string Currency);

/// <summary>Demande de retrait en attente de validation (vue admin, enrichie vendeur).</summary>
public sealed record PendingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? PayoutProvider,
    string? PayoutAccountNumber,
    DateTime CreatedAtUtc);

/// <summary>
/// Retrait dont le versement a été demandé au PSP mais N'EST PAS confirmé (vue
/// admin).
/// </summary>
public sealed record ProcessingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? ProviderRef,
    string? Anomaly,
    DateTime CreatedAtUtc,
    DateTime? SentToPspAtUtc);

/// <summary>Soldes du portefeuille de la plateforme (admin).</summary>
public sealed record PlatformWalletView(
    decimal CommissionBalance,
    decimal ProviderFeeBalance,
    decimal ShippingBalance,
    decimal RefundsBalance,
    string Currency);

/// <summary>Un remboursement client direct (versement MoMo initié par l'admin).</summary>
public sealed record CustomerRefundView(
    Guid Id,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Reason,
    string Msisdn,
    string Provider,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>Une ligne du grand livre wallet (relevé vendeur / plateforme).</summary>
public sealed record WalletTransactionView(
    Guid Id,
    string Account,
    string Direction,
    decimal Amount,
    string Currency,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime CreatedAtUtc);

/// <summary>Une demande de retrait vendeur.</summary>
public sealed record WithdrawalView(
    Guid Id,
    Guid SellerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);


// LE PORTEFEUILLE CLIENT (D33).

/// <summary>Solde du portefeuille d'un client.</summary>
public sealed record CustomerWalletView(
    Guid CustomerId,
    decimal AvailableBalance,

    // Total remboursé depuis toujours, virements sortis compris.
    decimal LifetimeRefunded,

    // Somme des demandes de virement EN ATTENTE de décision : les fonds ont déjà
    // quitté le solde disponible.
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Une demande de virement d'un client vers son Mobile Money.</summary>
public sealed record CustomerWithdrawalView(
    Guid Id,
    Guid CustomerId,
    decimal Amount,
    string Currency,

    // Destination FIGÉE à la demande : c'est elle, et rien d'autre, que
    // l'administrateur recopie chez le prestataire.
    string Msisdn,
    string Provider,
    string Status,

    // Référence du virement saisie par l'administrateur : la SEULE preuve que
    // l'argent est parti — aucun webhook ne confirmera ce versement.
    string? ExternalReference,
    string? AdminNote,
    DateTime RequestedAtUtc,
    DateTime? DecidedAtUtc,
    Guid? DecidedByUserId);

/// <summary>Ce que rend un crédit de remboursement au portefeuille d'un client.</summary>
public sealed record CustomerWalletCreditResult(
    Guid TransactionId,
    decimal NewBalance,
    string Currency,
    bool AlreadyApplied);

/// <summary>PAR OÙ L'ARGENT REVIENT AU CLIENT QUAND LE PRESTATAIRE NE SAIT PAS LE RENDRE.</summary>
public interface ICustomerWalletApi
{
    /// <summary>Rend un montant au client sur son portefeuille.</summary>
    Task<Result<CustomerWalletCreditResult>> CreditRefundAsync(
        Guid customerId, decimal amount, string currency, string reason,
        string idempotencyKey, CancellationToken cancellationToken = default);
}
