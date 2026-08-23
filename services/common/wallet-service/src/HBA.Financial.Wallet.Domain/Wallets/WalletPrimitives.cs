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
/// Cycle de vie d'une demande de virement d'un client (D33) :
///  • Requested : demandée par le client, fonds retenus, en attente d'un administrateur ;
///  • Paid      : l'administrateur a exécuté le virement CHEZ LE PRESTATAIRE et l'a
///                marqué payé, avec la référence du virement ;
///  • Rejected  : refusée par l'administrateur → les fonds sont restitués au portefeuille.
///
/// PAS DE `Processing`, CONTRAIREMENT AUX RETRAITS VENDEUR — ET C'EST LA
/// DIFFÉRENCE DE FOND, PAS UNE SIMPLIFICATION.
///
/// `WithdrawalStatus.Processing` existe parce qu'un payout FedaPay accepté ne veut
/// dire que « démarré » : il faut un état pour « l'argent est peut-être parti, on ne
/// sait pas encore », et une réconciliation pour trancher. Ici AUCUN appel PSP n'est
/// émis : c'est un humain qui exécute le virement et qui en saisit la référence. Il
/// n'y a donc jamais d'issue indéterminée à arbitrer, et un état qui n'aurait aucun
/// mécanisme pour en sortir serait un piège — des demandes bloquées à vie que rien
/// ne réconcilierait.
///
/// CE QUE CE MODÈLE NE COUVRE PAS : un administrateur peut marquer « payé » un
/// virement qu'il n'a pas fait, ou se tromper de destinataire. La référence externe
/// obligatoire (voir `CustomerWithdrawal.MarkPaid`) rend le rapprochement POSSIBLE ;
/// elle ne le fait pas. Ce rapprochement est un rapport d'exploitation à écrire,
/// nommé comme dette ouverte en D33.
/// </summary>
public enum CustomerWithdrawalStatus
{
    Requested = 0,
    Paid = 1,
    Rejected = 2
}

/// <summary>
/// Cycle de vie d'un remboursement client :
///  • Processing : payout FedaPay DEMANDÉ (start accepté ou issue indéterminée) ;
///    le portefeuille plateforme reste débité — on ne rembourse jamais tant qu'on
///    ne sait pas, sous peine de double versement.
///  • Completed  : versement confirmé par le PSP (statut « sent »).
///  • Failed     : rejet DÉFINITIF du PSP → le débit plateforme est contre-passé.
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
    /// plusieurs types de bénéficiaires — c'est ce qui a permis de ne pas créer
    /// une seconde table d'écritures pour les livreurs.
    /// </summary>
    Driver = 2,

    /// <summary>
    /// Client. Ajouté avec `CustomerWallet` (D33).
    ///
    /// CE SOLDE EST UNE DETTE DE LA PLATEFORME, PAS UN PRODUIT.
    ///
    /// Les trois autres types portent de l'argent que la plateforme DOIT à un
    /// partenaire ou qu'elle a encaissé. Celui-ci porte de l'argent déjà encaissé
    /// auprès du client et qu'on lui RE-DOIT : chaque crédit ici est un
    /// remboursement qu'aucun prestataire n'a su rendre par la carte ou le mobile.
    ///
    /// Le grand livre n'en fait aucune différence, et c'est voulu : c'est la même
    /// table, avec le même invariant. Ce qui manque — le rapprochement entre le
    /// total des soldes clients et la trésorerie réelle — n'est pas dans ce lot et
    /// est nommé comme tel en D33.
    /// </summary>
    Customer = 3,

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE MONDE EXTÉRIEUR — LA CONTREPARTIE QUI MANQUAIT (ISSUE-051).
    ///
    /// L'INVARIANT COMPTABLE DU §10.13 ÉTAIT ÉCRIT, TESTÉ, ET INAPPLICABLE.
    ///
    /// `WalletLedger.EnsureBalanced` vérifie que, dans une opération, la somme des
    /// débits égale celle des crédits. Aucun site ne pouvait l'appeler : une
    /// confirmation de commande n'écrivait QUE des crédits (vendeur, commission,
    /// frais), un reversement QUE un débit. La contrepartie — l'argent qui entre
    /// depuis la carte de l'acheteur, l'argent qui sort vers l'opérateur — n'était
    /// représentée nulle part. Le grand livre tenait une moitié de partie double.
    ///
    /// CE N'EST PAS UN PORTEFEUILLE, ET IL NE FAUT PAS EN FAIRE UN.
    ///
    /// Il n'a ni agrégat, ni solde stocké, ni `BalanceAfter` : son solde EST, par
    /// définition, le net de tout ce qui a traversé la plateforme. Lui donner une
    /// colonne de solde inviterait à la corriger à la main le jour d'un écart —
    /// c'est-à-dire à effacer la seule preuve qu'il y en a un.
    ///
    /// CE QU'IL NE REND PAS VRAI POUR AUTANT.
    ///
    /// Une écriture externe dit « tant est entré » ou « tant est sorti » SELON NOUS.
    /// Rien ne la rapproche encore d'un relevé de prestataire. L'invariant garantit
    /// la cohérence INTERNE d'une opération, pas sa véracité — c'est déjà ce qui
    /// manquait, ce n'est pas un rapprochement bancaire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    External = 4
}

/// <summary>
/// Sous-compte visé par une écriture. Vendeur : Pending (solde à venir) et
/// Available (solde principal retirable). Plateforme : Commission, Provider
/// (frais provider encaissés) et Shipping (frais de livraison encaissés).
/// </summary>
public enum WalletAccount
{
    Pending = 0,
    Available = 1,
    Commission = 2,
    Shipping = 3,
    Provider = 4,

    /// <summary>Plateforme : total reversé aux clients en remboursements directs (coût).</summary>
    Refunds = 5,

    /// <summary>
    /// Le compte de contrepartie du monde extérieur. Toujours associé à
    /// <see cref="WalletOwnerType.External"/> — voir son encadré : c'est lui qui
    /// rend l'invariant du §10.13 applicable.
    /// </summary>
    External = 6
}

/// <summary>Sens d'une écriture (crédit augmente le solde, débit le diminue).</summary>
public enum WalletDirection
{
    Credit = 0,
    Debit = 1
}

/// <summary>
/// Cycle de vie d'un retrait :
///  • Requested : demandé par le vendeur, en attente de validation admin (fonds retenus).
///  • Completed : validé par l'admin et payé (payout FedaPay réussi).
///  • Failed    : validé mais échec du payout (fonds recrédités).
///  • Rejected  : refusé par l'admin (fonds recrédités).
/// (Pending : ancien statut des retraits auto — conservé pour les données historiques.)
/// </summary>
public enum WithdrawalStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Requested = 3,
    Rejected = 4,

    /// <summary>
    /// Versement DEMANDÉ au PSP mais pas encore confirmé. État essentiel : un « start »
    /// FedaPay accepté ne veut dire que « started », pas « reçu ». Les fonds restent
    /// débités du vendeur ; la réconciliation clôturera en Completed (statut « sent »)
    /// ou en Failed avec remboursement (statut « failed »).
    /// Couvre aussi les issues INDÉTERMINÉES (timeout) : on ne rembourse pas, car
    /// l'argent est peut-être parti — rembourser autoriserait un double versement.
    /// </summary>
    Processing = 5
}
