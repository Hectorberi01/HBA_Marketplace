using HBA.Gateway.Application.Contracts.Merchant;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>merchant-service</c> — vendeurs, boutiques, KYB.</summary>
public interface IMerchantClient : IServiceClient
{
    /// <summary>Vitrine publique d'une boutique.</summary>
    Task<ServiceResult<StoreShowcase>> GetStoreShowcaseAsync(
        Guid storeId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/merchants/me</c> — AUTHENTIFIÉ, résout depuis le jeton.</summary>
    Task<ServiceResult<SellerAccount>> GetMySellerAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/merchants/{sellerId}/stores/</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<IReadOnlyList<MerchantStore>>> ListStoresAsync(
        Guid sellerId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/merchants/{sellerId}/stores/{storeId}</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<MerchantStore>> GetStoreAsync(
        Guid sellerId, Guid storeId, CancellationToken cancellationToken);
}
