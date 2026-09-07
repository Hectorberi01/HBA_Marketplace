namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>Identité forte d'un portefeuille vendeur.</summary>
public readonly record struct SellerWalletId(Guid Value)
{
    public static SellerWalletId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un retrait vendeur.</summary>
public readonly record struct WithdrawalId(Guid Value)
{
    public static WithdrawalId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un remboursement client (versement MoMo initié par l'admin).</summary>
public readonly record struct CustomerRefundId(Guid Value)
{
    public static CustomerRefundId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un portefeuille client (un par client).</summary>
public readonly record struct CustomerWalletId(Guid Value)
{
    public static CustomerWalletId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'une demande de virement d'un client vers son Mobile Money.</summary>
public readonly record struct CustomerWithdrawalId(Guid Value)
{
    public static CustomerWithdrawalId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Cycle de vie d'une demande de virement d'un client (D33) : • Requested :
/// demandée par le client, fonds retenus, en attente d'un administrateur ; • Paid :
/// l'administrateur a exécuté le virement CHEZ LE PRESTATAIRE et l'a marqué payé,
/// avec la référence du virement ; • Rejected : refusée par l'administrateur → les
/// fonds sont restitués au portefeuille.
/// </summary>
public enum CustomerWithdrawalStatus
{
    Requested = 0,
    Paid = 1,
    Rejected = 2
}

/// <summary>
/// Cycle de vie d'un remboursement client : • Processing : payout FedaPay DEMANDÉ
/// (start accepté ou issue indéterminée) ; le portefeuille plateforme reste débité
/// — on ne rembourse jamais tant qu'on ne sait pas, sous peine de double versement.
/// </summary>
public enum CustomerRefundStatus
{
    Processing = 0,
    Completed = 1,
    Failed = 2
}

/// <summary>Type de propriétaire d'une écriture au grand livre du wallet.</summary>
public enum WalletOwnerType
{
    Seller = 0,
    Platform = 1,

    /// <summary>
    /// Livreur. Ajouté avec DriverWallet : le grand livre était déjà prévu pour
    /// plusieurs types de bénéficiaires — c'est ce qui a permis de ne pas créer une
    /// seconde table d'écritures pour les livreurs.
    /// </summary>
    Driver = 2,

    /// <summary>Client. Ajouté avec `CustomerWallet` (D33).</summary>
    Customer = 3,

    /// <summary>LE MONDE EXTÉRIEUR — LA CONTREPARTIE QUI MANQUAIT (ISSUE-051).</summary>
    External = 4
}

/// <summary>Sous-compte visé par une écriture.</summary>
public enum WalletAccount
{
    Pending = 0,
    Available = 1,
    Commission = 2,
    Shipping = 3,
    Provider = 4,

    /// <summary>Plateforme : total reversé aux clients en remboursements directs (coût).</summary>
    Refunds = 5,

    /// <summary>Le compte de contrepartie du monde extérieur.</summary>
    External = 6
}

/// <summary>Sens d'une écriture (crédit augmente le solde, débit le diminue).</summary>
public enum WalletDirection
{
    Credit = 0,
    Debit = 1
}

/// <summary>
/// Cycle de vie d'un retrait : • Requested : demandé par le vendeur, en attente de
/// validation admin (fonds retenus).
/// </summary>
public enum WithdrawalStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Requested = 3,
    Rejected = 4,

    /// <summary>Versement DEMANDÉ au PSP mais pas encore confirmé.</summary>
    Processing = 5
}
