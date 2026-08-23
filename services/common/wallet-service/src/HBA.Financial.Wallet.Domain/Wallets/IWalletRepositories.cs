namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>Accès au portefeuille vendeur (un par vendeur, créé à la volée).</summary>
public interface ISellerWalletRepository
{
    Task<SellerWallet?> GetBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les portefeuilles de plusieurs vendeurs, en UNE lecture, indexés par vendeur.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ELLE EXISTE PARCE QUE DEUX CHEMINS APPELAIENT `GetBySellerAsync` EN
    ///     BOUCLE, SUR AUTANT DE TOURS QUE LA PLATEFORME A DE VENDEURS (§11).
    ///
    /// La création d'un lot de reversement, et son ANNULATION. Cinq mille vendeurs
    /// = cinq mille allers-retours, dans les deux sens. L'audit n'avait vu que le
    /// premier ; le second fait exactement la même chose, et il rend de l'argent.
    ///
    /// UN VENDEUR ABSENT DU DICTIONNAIRE N'A PAS DE PORTEFEUILLE — ce n'est pas
    /// un cas d'erreur ici, c'est une réponse. Les deux appelants la traitent :
    /// l'un refuse, l'autre journalise en `Error` et poursuit, parce qu'interrompre
    /// une annulation à mi-course laisserait le lot dans un état pire.
    ///
    /// SUIVI EF ACTIVÉ, comme `GetBySellerAsync` : les deux appelants MUTENT les
    /// portefeuilles rendus. Un `AsNoTracking` ici ferait échouer silencieusement
    /// tous les crédits — ils ne seraient jamais persistés.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, SellerWallet>> ListBySellersAsync(
        IReadOnlyCollection<Guid> sellerIds, CancellationToken cancellationToken = default);

    Task AddAsync(SellerWallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>
/// Accès au portefeuille livreur (un par livreur, créé à la première course).
/// </summary>
public interface IDriverWalletRepository
{
    Task<DriverWallet?> GetByDriverAsync(Guid driverId, CancellationToken cancellationToken = default);
    Task AddAsync(DriverWallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>
/// Accès au portefeuille client (un par client, créé à la volée au PREMIER crédit).
///
/// « CRÉÉ À LA VOLÉE » EXIGE L'INDEX UNIQUE SUR `CustomerId`.
///
/// Le service de mutation crée le portefeuille quand il n'en trouve pas. Sous
/// concurrence, deux remboursements simultanés sur un client qui n'en a pas encore
/// lisent tous deux « absent » et en créent DEUX : le solde se scinde, l'un des deux
/// devient invisible — et c'est de l'argent dû au client. Voir
/// `CustomerWalletConfiguration`.
/// </summary>
public interface ICustomerWalletRepository
{
    Task<CustomerWallet?> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task AddAsync(CustomerWallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux demandes de virement des clients (D33).</summary>
public interface ICustomerWithdrawalRepository
{
    Task AddAsync(CustomerWithdrawal withdrawal, CancellationToken cancellationToken = default);

    /// <summary>Charge une demande par son identifiant (décision admin). SUIVI EF : l'entité est mutée juste après.</summary>
    Task<CustomerWithdrawal?> GetByIdAsync(CustomerWithdrawalId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerWithdrawal>> ListByCustomerAsync(
        Guid customerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>Liste les demandes d'un statut donné, tous clients confondus (file admin).</summary>
    Task<IReadOnlyList<CustomerWithdrawal>> ListByStatusAsync(
        CustomerWithdrawalStatus status, int take = 100, CancellationToken cancellationToken = default);
}

/// <summary>Accès au portefeuille plateforme (singleton).</summary>
public interface IPlatformWalletRepository
{
    Task<PlatformWallet?> GetAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PlatformWallet wallet, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux demandes de retrait vendeur.</summary>
public interface IWithdrawalRepository
{
    Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Withdrawal>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);
    /// <summary>Charge une demande de retrait par son identifiant (validation admin).</summary>
    Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default);
    /// <summary>Liste les retraits d'un statut donné, tous vendeurs confondus (file admin).</summary>
    /// <summary>
    /// La file d'un statut, les plus anciennes d'abord, bornée.
    /// </summary>
    /// <remarks>
    /// LA BORNE PROTÈGE SURTOUT SES APPELANTS. `WalletQueries` itère sur ce
    /// résultat et interroge seller-service pour CHAQUE ligne — jusqu'à deux
    /// allers-retours par demande. Borner la file borne la boucle.
    /// </remarks>
    Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(
        WithdrawalStatus status, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retraits « en cours » (versement demandé au PSP, non confirmé), avec SUIVI EF :
    /// la réconciliation les mute (clôture ou échec + remboursement), il ne faut donc
    /// pas de AsNoTracking ici.
    /// </summary>
    Task<IReadOnlyList<Withdrawal>> ListProcessingForReconciliationAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrait porteur d'une référence de dépôt PSP (corrélation d'un webhook payout).
    /// SUIVI EF activé : l'entité est mutée juste après.
    /// </summary>
    Task<Withdrawal?> GetByProviderRefAsync(string providerRef, CancellationToken cancellationToken = default);
}

/// <summary>Grand livre des mouvements de wallet (vendeur et plateforme).</summary>
public interface IWalletTransactionRepository
{
    Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(Guid ownerId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une écriture existe-t-elle déjà pour cette référence ? — LE VERROU D'IDEMPOTENCE.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// L'outbox livre AT-LEAST-ONCE : si l'un des handlers d'un événement échoue, le
    /// message est rejoué EN ENTIER — y compris les handlers qui avaient réussi.
    ///
    /// Sans ce garde-fou, un rejeu de « remboursement versé » débitait le vendeur une
    /// SECONDE fois et restituait la commission deux fois. Une perte comptable réelle,
    /// silencieuse, et d'autant plus perverse qu'elle survenait dans le code même qui
    /// devait fiabiliser les remboursements.
    ///
    /// Le grand livre sert ici de REGISTRE : s'il porte déjà une écriture pour ce
    /// remboursement, c'est qu'il a déjà été contre-passé. On ne recommence pas.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    Task<bool> ExistsForReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// L'écriture déjà passée pour cette référence, s'il y en a une.
    ///
    /// POURQUOI UN `bool` NE SUFFISAIT PLUS.
    ///
    /// `ExistsForReferenceAsync` répond « c'est déjà fait », ce qui suffit aux
    /// contre-passations : elles n'ont rien à rendre à l'appelant. Le crédit de
    /// remboursement client, lui, DOIT rendre au rejeu EXACTEMENT le résultat du
    /// premier appel — l'identifiant de l'opération et le solde — sans quoi
    /// payment-service, qui réessaie, obtiendrait un succès vide et n'aurait rien à
    /// inscrire dans son propre dossier de remboursement.
    ///
    /// Rend la PREMIÈRE écriture trouvée : les flux qui utilisent cette lecture
    /// n'en produisent qu'une par référence, et leur index unique partiel le
    /// garantit en base.
    /// </summary>
    Task<WalletTransaction?> FindByReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default);
}
