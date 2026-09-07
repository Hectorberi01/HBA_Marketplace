using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// DEMANDE DE VIREMENT D'UN CLIENT DEPUIS SON PORTEFEUILLE VERS SON MOBILE MONEY.
/// </summary>
public sealed class CustomerWithdrawal : AggregateRoot<CustomerWithdrawalId>
{
    // ctor EF.
    private CustomerWithdrawal()
    {
    }

    private CustomerWithdrawal(
        CustomerWithdrawalId id, Guid customerId, decimal amount, string currency,
        string msisdn, string provider, string idempotencyKey)
        : base(id)
    {
        CustomerId = customerId;
        Amount = amount;
        Currency = currency;
        Msisdn = msisdn;
        Provider = provider;
        IdempotencyKey = idempotencyKey;
        Status = CustomerWithdrawalStatus.Requested;
        RequestedAtUtc = DateTime.UtcNow;
    }

    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>LA DESTINATION DU VIREMENT, FIGÉE À LA DEMANDE.</summary>
    public string Msisdn { get; private set; } = default!;

    /// <summary>Opérateur Mobile Money visé, figé à la demande.</summary>
    public string Provider { get; private set; } = default!;

    public CustomerWithdrawalStatus Status { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Instant de la décision de l'administrateur (paiement ou refus).</summary>
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Administrateur qui a tranché.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>
    /// Référence du virement saisie par l'administrateur (identifiant de
    /// transaction chez le prestataire, numéro de bordereau…).
    /// </summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Motif du refus, ou note libre accompagnant le paiement.</summary>
    public string? AdminNote { get; private set; }

    /// <summary>
    /// Clé d'idempotence de la DEMANDE, telle que le client l'a envoyée dans
    /// l'en-tête `Idempotency-Key` (§5).
    /// </summary>
    public string IdempotencyKey { get; private set; } = default!;

    /// <summary>Vrai tant que la demande attend la décision de l'administrateur.</summary>
    public bool IsPendingDecision => Status == CustomerWithdrawalStatus.Requested;

    public static CustomerWithdrawal Create(
        Guid customerId, decimal amount, string currency, string msisdn, string provider, string idempotencyKey)
        => new(CustomerWithdrawalId.New(), customerId, amount,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant(),
            msisdn.Trim(), provider.Trim().ToLowerInvariant(), idempotencyKey);

    /// <summary>
    /// L'administrateur a exécuté le virement chez le prestataire et le déclare
    /// payé.
    /// </summary>
    public Result MarkPaid(Guid adminId, string externalReference, DateTime nowUtc)
    {
        if (Status != CustomerWithdrawalStatus.Requested)
        {
            return Result.Failure(Error.Conflict(
                "wallet.customer_withdrawal.not_pending",
                "Cette demande de virement a déjà été tranchée."));
        }

        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.reference_required",
                "La référence du virement est obligatoire : sans elle, « payé » n'est vérifiable nulle part."));
        }

        if (adminId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.admin_required",
                "L'auteur de la décision est obligatoire."));
        }

        Status = CustomerWithdrawalStatus.Paid;
        ExternalReference = externalReference.Trim();
        DecidedByUserId = adminId;
        DecidedAtUtc = nowUtc;

        return Result.Success();
    }

    /// <summary>Refus par l'administrateur.</summary>
    public Result Reject(Guid adminId, string reason, DateTime nowUtc)
    {
        if (Status != CustomerWithdrawalStatus.Requested)
        {
            return Result.Failure(Error.Conflict(
                "wallet.customer_withdrawal.not_pending",
                "Cette demande de virement a déjà été tranchée."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.reason_required",
                "Un motif de refus est obligatoire."));
        }

        if (adminId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer_withdrawal.admin_required",
                "L'auteur de la décision est obligatoire."));
        }

        Status = CustomerWithdrawalStatus.Rejected;
        AdminNote = reason.Trim();
        DecidedByUserId = adminId;
        DecidedAtUtc = nowUtc;

        return Result.Success();
    }
}
