namespace HBA.Merchants.Domain.Sellers;

public interface ISellerRepository
{
    Task AddAsync(Seller seller, CancellationToken cancellationToken = default);

    void Remove(Seller seller);

    Task<Seller?> GetByIdAsync(SellerId id, CancellationToken cancellationToken = default);

    Task<Seller?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> ShopNameExistsAsync(string shopName, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA FILE D'ADMINISTRATION : UNE PAGE, DES FILTRES, ET LES COMPTEURS.
    ///
    /// ELLE REMPLACE UN `ListAsync()` QUI RENDAIT LA TABLE ENTIÈRE.
    ///
    /// Avec `.Include(KybDocuments)`, et sans le moindre filtre — pas même sur
    /// `KybStatus`, la seule chose qu'un modérateur cherche. C'était l'unique
    /// entrée de la file de validation KYB.
    /// </summary>
    /// <param name="search">Sous-chaîne du nom de boutique, insensible à la casse. Nul = tout.</param>
    /// <param name="kybStatus">Filtre sur l'état du dossier. Nul = tout.</param>
    /// <param name="status">Filtre sur l'état commercial. Nul = tout.</param>
    /// <returns>
    /// La page, le total APRÈS filtres, et le comptage par `KybStatus` — ce
    /// dernier calculé sur la recherche SANS le filtre de statut, sans quoi la
    /// facette sélectionnée serait la seule non nulle et les autres afficheraient
    /// zéro alors qu'il y a du travail.
    /// </returns>
    Task<(IReadOnlyList<Seller> Items, int Total, IReadOnlyDictionary<string, int> KybFacets)> ListPagedAsync(
        int page,
        int pageSize,
        string? search,
        KybStatus? kybStatus,
        SellerStatus? status,
        CancellationToken cancellationToken = default);
}
