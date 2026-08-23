using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Écriture immuable au grand livre du wallet : trace chaque mouvement de solde
/// (vendeur ou plateforme) avec son sens, son motif et la référence d'origine
/// (commande, retrait). Sert d'historique/relevé côté vendeur et admin.
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// GROUPE LES ÉCRITURES D'UNE MÊME OPÉRATION (§10.13, `ledger_entries.transaction_id`).
    ///
    /// Un remboursement débite le vendeur sur DEUX comptes — l'en-cours puis le
    /// disponible — et débite la plateforme de sa commission. Sans identifiant
    /// commun, ces trois lignes sont trois faits indépendants : impossible de
    /// vérifier qu'elles s'équilibrent, impossible de les annuler ensemble, et
    /// impossible de répondre à « qu'est-ce qui a produit ce mouvement ? »
    /// autrement qu'en rapprochant des horodatages.
    ///
    /// VALEUR PAR DÉFAUT : L'ÉCRITURE EST SA PROPRE OPÉRATION.
    ///
    /// Les appelants existants n'ont pas été regroupés d'un coup — ils écrivent
    /// une ligne à la fois et continuent de le faire. Leur donner un identifiant
    /// distinct est EXACT : ils ne forment effectivement pas une opération
    /// commune. Le regroupement se fait site par site, là où l'opération existe
    /// vraiment, et `DebitSellerForRefundAsync` est le premier.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid TransactionId { get; private set; }

    /// <summary>
    /// Solde du compte APRÈS cette écriture (§10.13, `ledger_entries.balance_after`).
    ///
    /// Null pour les écritures antérieures et pour celles dont l'appelant ne le
    /// connaît pas encore. C'est ce champ qui permet de rejouer un solde à une date
    /// donnée sans rejouer tout l'historique — et surtout de détecter une dérive
    /// entre la somme des mouvements et le solde stocké.
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

    /// <summary>
    /// Écriture au crédit ou au débit d'un livreur.
    ///
    /// Le compte est toujours <c>Available</c> : contrairement au vendeur, le
    /// livreur n'a pas de solde « en attente ». Sa course est faite ou ne l'est
    /// pas — voir l'encadré de DriverWallet.
    /// </summary>
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
    ///
    /// Le compte est toujours <c>Available</c> : un remboursement est acquis à la
    /// seconde où il est décidé, il n'y a donc pas de solde « à venir » à distinguer.
    /// Voir l'encadré de <see cref="CustomerWallet"/>.
    ///
    /// CES ÉCRITURES SONT DES DETTES, PAS DES PRODUITS. Voir
    /// <c>WalletOwnerType.Customer</c> : le grand livre ne fait aucune différence, et
    /// le rapprochement entre le total des soldes clients et la trésorerie réelle
    /// n'existe pas encore (D33).
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
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS ELLE, L'INVARIANT DU §10.13 NE POUVAIT ÊTRE APPELÉ NULLE PART.
    ///
    /// Une confirmation de commande n'écrivait QUE des crédits — net vendeur,
    /// commission, frais provider, frais de port. Un reversement, QUE un débit.
    /// `WalletLedger.EnsureBalanced` aurait échoué sur chacune, à juste titre : la
    /// moitié de chaque opération manquait. Elle manquait parce que la contrepartie
    /// n'est pas un portefeuille de la plateforme, mais le monde extérieur.
    ///
    /// PAS DE `BalanceAfter`, ET C'EST STRUCTUREL.
    ///
    /// Ce compte n'a pas de solde stocké à reporter : son solde est, par définition,
    /// le net de tout ce qui a traversé la plateforme. Le paramètre n'existe donc
    /// pas — l'oublier serait un choix, ne pas pouvoir le passer est un fait.
    ///
    /// `OwnerId` VAUT `Guid.Empty`, ET IL N'Y EN A QU'UN.
    ///
    /// Distinguer « FedaPay » de « Stripe » ici serait une autre décision, utile le
    /// jour d'un rapprochement par prestataire. Elle n'est pas prise : le
    /// prestataire se lit déjà sur le paiement, et inventer un identifiant de
    /// propriétaire par PSP ferait croire à un compte qu'on ne tient pas.
    /// ═════════════════════════════════════════════════════════════════════════
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
