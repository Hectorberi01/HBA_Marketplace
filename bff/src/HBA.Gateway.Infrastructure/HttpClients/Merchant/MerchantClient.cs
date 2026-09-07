using System.Net;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Merchant;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Merchant;

/// <inheritdoc cref="IMerchantClient" />
public sealed class MerchantClient : ServiceHttpClient, IMerchantClient
{
    /// <summary>Code rendu tant qu'aucune route publique de boutique n'existe.</summary>
    private const int NotImplemented = (int)HttpStatusCode.NotImplemented;

    public MerchantClient(HttpClient http, ILogger<MerchantClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Merchant;

    // CE CLIENT NE PASSE PAS PAR YARP, DONC PAS PAR LA COQUILLE DE DÉPRÉCIATION.

    public Task<ServiceResult<StoreShowcase>> GetStoreShowcaseAsync(
        Guid storeId, CancellationToken cancellationToken)
        => Task.FromResult(ServiceResult<StoreShowcase>.Failure(
            NotImplemented,
            "merchant-service n'expose aucune vitrine publique de boutique"));

    // AUCUN IDENTIFIANT DANS L'URL : le service résout le vendeur depuis le jeton,
    // que la propagation d'en-têtes transmet.
    public Task<ServiceResult<SellerAccount>> GetMySellerAsync(CancellationToken cancellationToken)
        => GetAsync<SellerAccount>("/api/v1/merchants/me", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<MerchantStore>>> ListStoresAsync(
        Guid sellerId, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<MerchantStore>>(
            $"/api/v1/merchants/{sellerId}/stores/", cancellationToken);

    public Task<ServiceResult<MerchantStore>> GetStoreAsync(
        Guid sellerId, Guid storeId, CancellationToken cancellationToken)
        => GetAsync<MerchantStore>(
            $"/api/v1/merchants/{sellerId}/stores/{storeId}", cancellationToken);
}
