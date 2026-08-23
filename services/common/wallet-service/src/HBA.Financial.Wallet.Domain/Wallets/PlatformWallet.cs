using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Portefeuille de la plateforme (admin). Agrégat singleton identifié par
/// <see cref="SingletonId"/>. Deux soldes distincts :
///  • <see cref="CommissionBalance"/> : commissions encaissées sur chaque vente.
///  • <see cref="ProviderFeeBalance"/> : frais provider encaissés sur chaque vente.
///  • <see cref="ShippingBalance"/> : frais de livraison encaissés par la plateforme.
/// </summary>
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
    /// hors flux retour). C'est un COÛT cumulé pour la plateforme : il croît à chaque
    /// remboursement confirmé et se contre-passe si un payout échoue.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SORT DU SOLDE LIVRAISON : PART DU LIVREUR, OU COMMANDE ANNULÉE.
    ///
    /// CE DÉBIT N'EXISTAIT PAS, ET LE SOLDE ÉTAIT UNE RECETTE DÉGUISÉE EN MARGE.
    ///
    /// `CreditShipping` enregistrait le montant INTÉGRAL encaissé auprès du client.
    /// Rien ne le diminuait jamais : ni la part versée au livreur — qui en
    /// représente l'essentiel — ni le remboursement d'une commande annulée.
    ///
    /// Sur un repas dont la course coûte 2 000 francs, la plateforme affichait
    /// 2 000 de « frais de livraison » alors que 1 400 partaient au coursier. Le
    /// solde surestimait la marge réelle d'un facteur trois, et le seul moyen de
    /// s'en apercevoir était de comparer deux grands livres à la main.
    ///
    /// Ce compte doit se lire comme un résultat, pas comme un chiffre d'affaires :
    /// ce qui rentre moins ce qui sort.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public void DebitShipping(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        // LE SOLDE PEUT DEVENIR NÉGATIF, ET C'EST VOULU.
        //
        // Une course peut coûter plus cher que le forfait facturé — c'est même le
        // cas nominal de la marchandise, dont les frais sont un forfait sans
        // rapport avec la distance. Borner à zéro masquerait précisément la perte
        // qu'on cherche à rendre visible.
        ShippingBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Restitue la commission encaissée sur une vente remboursée.
    ///
    /// La vente n'a pas eu lieu : la plateforme n'a rien à prendre dessus. Garder la
    /// commission sur une commande annulée reviendrait à se rémunérer sur un service
    /// non rendu — et fausserait durablement le chiffre d'affaires.
    ///
    /// Le solde peut devenir négatif si la commission a déjà été retirée. Ce n'est pas
    /// une anomalie : c'est une dette de la plateforme envers elle-même, qui se
    /// résorbe sur les ventes suivantes. La masquer serait pire que l'afficher.
    /// </summary>
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
    ///
    /// NUANCE COMPTABLE À CONNAÎTRE : les frais réellement prélevés par le PSP sur
    /// la transaction d'origine, eux, ne vous sont PAS rendus. Ce débit rétablit la
    /// symétrie de VOS écritures ; il ne récupère pas l'argent chez FedaPay. Le coût
    /// du transport de l'argent reste à votre charge sur chaque remboursement — c'est
    /// une perte réelle, à connaître avant de fixer votre politique de retours.
    public void DebitProviderFee(decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        ProviderFeeBalance -= amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Comptabilise un remboursement direct versé à un client (coût plateforme).
    /// Appelé à l'initiation du payout ; contre-passé par <see cref="ReverseRefund"/>
    /// si le versement échoue définitivement.
    /// </summary>
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
