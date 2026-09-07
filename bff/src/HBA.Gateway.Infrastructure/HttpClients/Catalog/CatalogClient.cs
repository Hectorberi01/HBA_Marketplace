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
    //
    //     publicCatalog = MapGroup("/api/v1/catalog")        .AllowAnonymous()
    //     GET /products/{id:guid}      → ProductSummary
    //     GET /categories              → IReadOnlyList<CategorySummary>
    //     GET /sellers/{id}/products   → PagedResult<ProductSummary>
    //
    // Le préfixe est écrit ici parce que l'adresse de base ne porte que l'hôte.
    //
    // ═════════════════════════════════════════════════════════════════════════
    // CE CLIENT NE PASSE PAS PAR YARP, DONC PAS PAR LA COQUILLE DE
    //    DÉPRÉCIATION.
    //
    // La bascule de catalog vers `/api/v1/catalog` s'accompagne d'une coquille à
    // la passerelle qui réécrit l'ancien chemin. Elle protège les clients EXTERNES
    // — applications mobiles, web. Elle ne protège PAS ce fichier : `HttpClient`
    // tape l'adresse du service en direct, le proxy n'est pas sur le chemin.
    //
    // Ces trois lignes seraient donc restées en 404 alors même que la vitrine
    // publique fonctionnait — et le symptôme se serait affiché sur l'accueil de
    // l'application et sur la fiche produit, c'est-à-dire loin d'ici.
    //
    // La règle à retenir pour la prochaine migration : chercher le préfixe du
    // service dans `HttpClients/`, PAS seulement dans les routes YARP.
    // ═════════════════════════════════════════════════════════════════════════
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
