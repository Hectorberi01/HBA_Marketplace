using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>LE SOLDE D'UN CLIENT.</summary>
public sealed class CustomerWallet : AggregateRoot<CustomerWalletId>
{
    // ctor EF.
    private CustomerWallet()
    {
    }

    private CustomerWallet(CustomerWalletId id, Guid customerId, string currency)
        : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        AvailableBalance = 0m;
        LifetimeRefunded = 0m;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid CustomerId { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>Ce que le client peut dépenser ou demander à faire virer aujourd'hui.</summary>
    public decimal AvailableBalance { get; private set; }

    /// <summary>Total remboursé depuis toujours, virements sortis compris.</summary>
    public decimal LifetimeRefunded { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerWallet Create(Guid customerId, string currency)
        => new(CustomerWalletId.New(), customerId,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    /// <summary>Crédite un remboursement.</summary>
    public Result CreditRefund(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer.amount_invalid",
                "Le montant d'un remboursement doit être strictement positif."));
        }

        AvailableBalance += amount;
        LifetimeRefunded += amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>Retient les fonds à la demande de virement.</summary>
    public Result Hold(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer.amount_invalid", "Le montant du virement demandé doit être positif."));
        }

        if (amount > AvailableBalance)
        {
            return Result.Failure(Error.Conflict(
                "wallet.customer.insufficient_balance",
                "Le montant demandé dépasse le solde disponible."));
        }

        AvailableBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>Restitue les fonds d'une demande refusée.</summary>
    public Result Restore(decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Validation(
                "wallet.customer.amount_invalid", "Le montant restitué doit être positif."));
        }

        AvailableBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;

        return Result.Success();
    }
}
