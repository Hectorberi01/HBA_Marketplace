using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>Demande de retrait d'un vendeur depuis son solde principal.</summary>
public sealed class Withdrawal : AggregateRoot<WithdrawalId>
{
    private Withdrawal()
    {
    }

    private Withdrawal(
        WithdrawalId id, Guid sellerId, decimal amount, string currency,
        string? payoutProvider, string? payoutAccountNumber, string? payoutAccountName)
        : base(id)
    {
        SellerId = sellerId;
        Amount = amount;
        Currency = currency;
        PayoutProvider = payoutProvider;
        PayoutAccountNumber = payoutAccountNumber;
        PayoutAccountName = payoutAccountName;
        Status = WithdrawalStatus.Requested;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Vrai tant que la demande attend la validation de l'admin.</summary>
    public bool IsPendingApproval => Status == WithdrawalStatus.Requested;

    /// <summary>Versement demandé au PSP, en attente de confirmation (réconciliation).</summary>
    public bool IsProcessing => Status == WithdrawalStatus.Processing;

    /// <summary>Date de la demande de versement au PSP (sert au délai d'alerte).</summary>
    public DateTime? SentToPspAtUtc { get; private set; }

    public Guid SellerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public WithdrawalStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>LA DESTINATION DU VIREMENT, FIGÉE À LA DEMANDE.</summary>
    public string? PayoutProvider { get; private set; }

    /// <summary>Numéro Mobile Money visé, figé à la demande.</summary>
    public string? PayoutAccountNumber { get; private set; }

    /// <summary>Nom du bénéficiaire, figé à la demande.</summary>
    public string? PayoutAccountName { get; private set; }

    /// <summary>Cette demande porte-t-elle une destination figée ?</summary>
    public bool HasFrozenDestination
        => !string.IsNullOrWhiteSpace(PayoutProvider) && !string.IsNullOrWhiteSpace(PayoutAccountNumber);

    /// <summary>La destination figée correspond-elle encore au compte du vendeur ?</summary>
    public bool MatchesDestination(string? provider, string? accountNumber)
        => string.Equals(PayoutProvider, provider, StringComparison.OrdinalIgnoreCase)
           && string.Equals(PayoutAccountNumber, accountNumber, StringComparison.Ordinal);

    public static Withdrawal Create(
        Guid sellerId, decimal amount, string currency,
        string payoutProvider, string payoutAccountNumber, string? payoutAccountName)
        => new(WithdrawalId.New(), sellerId, amount,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant(),
            payoutProvider, payoutAccountNumber, payoutAccountName);

    /// <summary>
    /// Le versement a été DEMANDÉ au PSP (créé + démarré), ou son issue est
    /// indéterminée.
    /// </summary>
    public void MarkProcessing(string? providerRef, string? note = null)
    {
        Status = WithdrawalStatus.Processing;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = note; // trace de l'anomalie éventuelle (timeout…)
        SentToPspAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>Versement CONFIRMÉ par le PSP (statut « sent »).</summary>
    public void Complete(string? providerRef)
    {
        Status = WithdrawalStatus.Completed;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = null;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = WithdrawalStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Refus par l'admin (les fonds sont recrédités par le handler).</summary>
    public void Reject(string reason)
    {
        Status = WithdrawalStatus.Rejected;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
