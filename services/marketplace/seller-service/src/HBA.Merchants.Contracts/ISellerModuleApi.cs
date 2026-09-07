namespace HBA.Merchants.Contracts;

/// <summary>API in-process publique du module Sellers.</summary>
public interface ISellerModuleApi
{
    Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Une boutique par son identifiant, ou null.</summary>
    Task<StoreSummary?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Les boutiques d'un vendeur — le multi-boutiques, vu de l'extérieur.</summary>
    Task<IReadOnlyList<StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>LE COMPTE DE REVERSEMENT — LE SEUL CHEMIN VALABLE POUR L'OBTENIR.</summary>
    Task<SellerPayout> GetSellerPayoutAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
