using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>Portefeuille de la plateforme (admin).</summary>
public sealed class PlatformWallet : AggregateRoot<Guid>
{
    /// <summary>Identifiant fixe du portefeuille plateforme unique.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-0000000000A1");

    private PlatformWallet()
    {
    }

    private PlatformWallet(Guid id, string currency)
        : base(id)
    {
        Currency = currency;
        CommissionBalance = 0m;
        ProviderFeeBalance = 0m;
        ShippingBalance = 0m;
        RefundsBalance = 0m;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public string Currency { get; private set; } = default!;
    public decimal CommissionBalance { get; private set; }
    public decimal ProviderFeeBalance { get; private set; }
    public decimal ShippingBalance { get; private set; }

    /// <summary>
    /// Total reversé aux clients en remboursements directs (initiés par l'admin,
    /// hors flux retour).
    /// </summary>
    public decimal RefundsBalance { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PlatformWallet Create(string currency)
        => new(SingletonId, string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant());

    public void CreditCommission(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        CommissionBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void CreditProviderFee(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        ProviderFeeBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void CreditShipping(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        ShippingBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>SORT DU SOLDE LIVRAISON : PART DU LIVREUR, OU COMMANDE ANNULÉE.</summary>
    public void DebitShipping(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        // LE SOLDE PEUT DEVENIR NÉGATIF, ET C'EST VOULU.
        ShippingBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Restitue la commission encaissée sur une vente remboursée.</summary>
    public void DebitCommission(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        CommissionBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Restitue les frais du prestataire sur une vente remboursée.</summary>
    public void DebitProviderFee(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        ProviderFeeBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Comptabilise un remboursement direct versé à un client (coût plateforme).</summary>
    public void AccrueRefund(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        RefundsBalance += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Annule un remboursement client comptabilisé dont le payout a échoué.</summary>
    public void ReverseRefund(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        RefundsBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
