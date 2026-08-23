namespace HBA.Financial.Billing.Domain.Invoices;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);

    Task<Invoice?> GetByIdAsync(InvoiceId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Invoice>> ListBySellerAsync(
        Guid sellerId, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une page de factures, tous vendeurs confondus, pour l'administration.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE MANQUAIT, ET AVEC ELLE TOUTE VUE PLATEFORME.
    ///
    /// `ListBySellerAsync` exige un vendeur ; `GetByIdAsync` exige une facture.
    /// Il n'existait donc aucun moyen de répondre à « quelles factures sont
    /// émises et impayées ce mois-ci » — la seule question qu'un administrateur
    /// pose à ce module.
    ///
    /// ELLE REND LE CHIFFRE D'AFFAIRES COMMISSIONNÉ, VENDEUR PAR VENDEUR.
    ///
    /// C'est pourquoi la route qui la sert porte `.RequireAdmin()`, et pourquoi
    /// la route de passerelle n'a été ouverte qu'APRÈS cette garde. L'ordre
    /// inverse aurait exposé la donnée à tout compte authentifié le temps d'un
    /// déploiement.
    ///
    /// Le compte par statut est calculé AVANT le filtre : sinon les facettes ne
    /// montreraient que le statut qu'on vient de choisir. Même choix que
    /// `UserRepository.ListPagedAsync` et `ReturnRequestRepository.ListForAdminAsync`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<(IReadOnlyList<Invoice> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForAdminAsync(
            int page, int pageSize, InvoiceStatus? status, Guid? sellerId,
            CancellationToken cancellationToken = default);
}
