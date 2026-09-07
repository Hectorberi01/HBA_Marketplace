using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>Portefeuille d'un vendeur.</summary>
public sealed class SellerWallet : AggregateRoot<SellerWalletId>
{
    private SellerWallet()
    {
    }

    private SellerWallet(SellerWalletId id, Guid sellerId, string currency)
        : base(id)
    {
        SellerId = sellerId;
        Currency = currency;
        PendingBalance = 0m;
        AvailableBalance = 0m;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid SellerId { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal PendingBalance { get; private set; }
    public decimal AvailableBalance { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SellerWallet Create(Guid sellerId, string currency)
        => new(SellerWalletId.New(), sellerId, Normalize(currency));

    /// <summary>Crédite le solde à venir (gain net d'une commande confirmée).</summary>
    public void CreditPending(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        PendingBalance += amount;
        Touch();
    }

    /// <summary>
    /// Déplace un montant du solde à venir vers le solde principal (livraison
    /// confirmée).
    /// </summary>
    public void ReleaseToAvailable(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        var moved = Math.Min(amount, PendingBalance);
        if (moved <= 0m)
        {
            return;
        }

        PendingBalance -= moved;
        AvailableBalance += moved;
        Touch();
    }

    /// <summary>Débite le solde principal pour un retrait.</summary>
    public Result Withdraw(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation("wallet.amount_invalid", "Le montant du retrait doit être positif."));
        }

        if (amount > AvailableBalance)
        {
            return Result.Failure(Error.Validation("wallet.insufficient_funds", "Solde principal insuffisant pour ce retrait."));
        }

        AvailableBalance -= amount;
        Touch();
        return Result.Success();
    }

    /// <summary>Recrédite le solde principal (annulation d'un retrait échoué).</summary>
    public void CreditAvailable(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        AvailableBalance += amount;
        Touch();
    }

    /// <summary>
    /// Contre-passe le gain d'un vendeur après un remboursement CLIENT réellement
    /// versé.
    /// </summary>
    /// <returns>Ce qui a été pris sur le solde à venir, et sur le solde principal.</returns>
    public (decimal FromPending, decimal FromAvailable) DebitForRefund(decimal amount)
    {
        if (amount <= 0m)
        {
            return (0m, 0m);
        }

        // 1. Le solde à venir d'abord : ce gain n'a pas encore été libéré, le
        // reprendre ne retire rien au vendeur — il n'y avait pas encore droit.
        var fromPending = Math.Min(amount, PendingBalance);
        PendingBalance -= fromPending;

        // 2. Le reste sur le solde principal — quitte à le rendre négatif.
        var fromAvailable = amount - fromPending;
        AvailableBalance -= fromAvailable;

        Touch();
        return (fromPending, fromAvailable);
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string Normalize(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
}
