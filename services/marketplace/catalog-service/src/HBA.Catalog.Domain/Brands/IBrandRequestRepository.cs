namespace HBA.Catalog.Domain.Brands;

public interface IBrandRequestRepository
{
    Task AddAsync(BrandRequest request, CancellationToken cancellationToken = default);

    Task<BrandRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>La file des demandes en attente, les plus anciennes d'abord.</summary>
    Task<IReadOnlyList<BrandRequest>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Une demande EN ATTENTE portant déjà ce nom, pour ce vendeur.
    ///
    /// SANS CE CONTRÔLE, UN VENDEUR REMPLIT LA FILE EN CLIQUANT DEUX FOIS.
    ///
    /// Le formulaire de demande est un champ et un bouton ; le double-clic est la
    /// règle, pas l'exception. Deux demandes identiques obligent l'administrateur à
    /// trancher deux fois — et la seconde approbation échouerait, la marque
    /// existant désormais.
    /// </summary>
    Task<BrandRequest?> GetPendingByNameAsync(
        Guid sellerId, string name, CancellationToken cancellationToken = default);
}
