namespace HBA.Catalog.Domain.Brands;

public interface IBrandRequestRepository
{
    Task AddAsync(BrandRequest request, CancellationToken cancellationToken = default);

    Task<BrandRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>La file des demandes en attente, les plus anciennes d'abord.</summary>
    Task<IReadOnlyList<BrandRequest>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Une demande EN ATTENTE portant déjà ce nom, pour ce vendeur.</summary>
    Task<BrandRequest?> GetPendingByNameAsync(
        Guid sellerId, string name, CancellationToken cancellationToken = default);
}
