using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Écriture immuable au grand livre du wallet : trace chaque mouvement de solde
/// (vendeur ou plateforme) avec son sens, son motif et la référence d'origine
/// (commande, retrait).
/// </summary>
public sealed class WalletTransaction : AggregateRoot<Guid>
{
    private WalletTransaction()
    {
    }

    private WalletTransaction(
        Guid id, Guid transactionId, Guid ownerId, WalletOwnerType ownerType, WalletAccount account,
        WalletDirection direction, decimal amount, string currency,
        string reason, string? referenceType, Guid? referenceId, decimal? balanceAfter)
        : base(id)
    {
        TransactionId = transactionId;
        BalanceAfter = balanceAfter;
        OwnerId = ownerId;
        OwnerType = ownerType;
        Account = account;
        Direction = direction;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// GROUPE LES ÉCRITURES D'UNE MÊME OPÉRATION (§10.13,
    /// `ledger_entries.transaction_id`).
    /// </summary>
    public Guid TransactionId { get; private set; }

    /// <summary>
    /// Solde du compte APRÈS cette écriture (§10.13,
    /// `ledger_entries.balance_after`).
    /// </summary>
    public decimal? BalanceAfter { get; private set; }

    public Guid OwnerId { get; private set; }
    public WalletOwnerType OwnerType { get; private set; }
    public WalletAccount Account { get; private set; }
    public WalletDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static WalletTransaction ForSeller(
        Guid sellerId, WalletAccount account, WalletDirection direction, decimal amount,
        string currency, string reason, string? referenceType = null, Guid? referenceId = null,
        Guid? transactionId = null, decimal? balanceAfter = null)
        => new(Guid.NewGuid(), transactionId ?? Guid.NewGuid(), sellerId, WalletOwnerType.Seller,
            account, direction, amount, Normalize(currency), reason, referenceType, referenceId, balanceAfter);

    /// <summary>Écriture au crédit ou au débit d'un livreur.</summary>
    public static WalletTransaction ForDriver(
        Guid driverId, WalletDirection direction, decimal amount,
        string currency, string reason, string? referenceType = null, Guid? referenceId = null,
        Guid? transactionId = null, decimal? balanceAfter = null)
        => new(Guid.NewGuid(), transactionId ?? Guid.NewGuid(), driverId, WalletOwnerType.Driver,
            WalletAccount.Available, direction, amount, Normalize(currency), reason, referenceType,
            referenceId, balanceAfter);

    /// <summary>
    /// Écriture au crédit ou au débit d'un CLIENT (remboursement rendu, virement
    /// retenu, virement refusé et restitué).
    /// </summary>
    public static WalletTransaction ForCustomer(
        Guid customerId, WalletDirection direction, decimal amount,
        string currency, string reason, string? referenceType = null, Guid? referenceId = null,
        Guid? transactionId = null, decimal? balanceAfter = null)
        => new(Guid.NewGuid(), transactionId ?? Guid.NewGuid(), customerId, WalletOwnerType.Customer,
            WalletAccount.Available, direction, amount, Normalize(currency), reason, referenceType,
            referenceId, balanceAfter);

    public static WalletTransaction ForPlatform(
        WalletAccount account, WalletDirection direction, decimal amount,
        string currency, string reason, string? referenceType = null, Guid? referenceId = null,
        Guid? transactionId = null, decimal? balanceAfter = null)
        => new(Guid.NewGuid(), transactionId ?? Guid.NewGuid(), PlatformWallet.SingletonId,
            WalletOwnerType.Platform, account, direction, amount, Normalize(currency), reason,
            referenceType, referenceId, balanceAfter);

    /// <summary>
    /// Écriture de CONTREPARTIE : l'argent qui entre depuis l'acheteur, ou qui sort
    /// vers un opérateur.
    /// </summary>
    public static WalletTransaction ForExternal(
        WalletDirection direction, decimal amount, string currency, string reason,
        string? referenceType = null, Guid? referenceId = null, Guid? transactionId = null)
        => new(Guid.NewGuid(), transactionId ?? Guid.NewGuid(), Guid.Empty, WalletOwnerType.External,
            WalletAccount.External, direction, amount, Normalize(currency), reason,
            referenceType, referenceId, balanceAfter: null);

    private static string Normalize(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
}
