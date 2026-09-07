using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

public readonly record struct DriverWalletId(Guid Value)
{
    public static DriverWalletId New() => new(Guid.NewGuid());
}

/// <summary>LE SOLDE D'UN LIVREUR.</summary>
public sealed class DriverWallet : AggregateRoot<DriverWalletId>
{
    // ctor EF.
    private DriverWallet()
    {
    }

    private DriverWallet(DriverWalletId id, Guid driverId, string currency)
        : base(id)
    {
        DriverId = driverId;
        Currency = currency;
        AvailableBalance = 0m;
        LifetimeEarned = 0m;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid DriverId { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>Ce que le livreur peut retirer aujourd'hui.</summary>
    public decimal AvailableBalance { get; private set; }

    /// <summary>Total gagné depuis l'inscription, retraits compris.</summary>
    public decimal LifetimeEarned { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static DriverWallet Create(Guid driverId, string currency)
        => new(DriverWalletId.New(), driverId, string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    /// <summary>Crédite le gain d'une course.</summary>
    public Result CreditEarning(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.driver.amount_invalid",
                "Le gain d'une course doit être strictement positif."));
        }

        AvailableBalance += amount;
        LifetimeEarned += amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>Retire du solde.</summary>
    public Result Withdraw(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.driver.amount_invalid", "Le montant du retrait doit être positif."));
        }

        if (amount > AvailableBalance)
        {
            return Result.Failure(Error.Conflict(
                "wallet.driver.insufficient_balance",
                "Le montant demandé dépasse le solde disponible."));
        }

        AvailableBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }
}
