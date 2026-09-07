using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Catalog;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Catalog;

/// <inheritdoc cref="ICatalogClient" />
public sealed class CatalogClient : ServiceHttpClient, ICatalogClient
{
    public CatalogClient(HttpClient http, ILogger<CatalogClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Catalog;

    // CHEMINS RELEVÉS DANS `CatalogEndpoints.cs`, PAS DEVINÉS.
    public Task<ServiceResult<CatalogProduct>> GetProductAsync(
        Guid id, CancellationToken cancellationToken)
        => GetAsync<CatalogProduct>($"/api/v1/catalog/products/{id}", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<CatalogCategory>>> ListCategoriesAsync(
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<CatalogCategory>>("/api/v1/catalog/categories", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<CatalogProduct>>> ListSellerProductsAsync(
        Guid sellerId, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<CatalogProduct>>(
            $"/api/v1/catalog/sellers/{sellerId}/products", cancellationToken);
}
