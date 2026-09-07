using HBA.Gateway.Application.Contracts.Catalog;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>catalog-service</c> — produits, catégories, marques.</summary>
/// <remarks>
/// Interface distincte plutôt qu'un <see cref="IServiceClient"/> nommé : elle
/// permet d'attacher à CE service ses propres délais, son propre disjoncteur et
/// sa propre politique de réessai. Un catalogue lent ne doit pas imposer ses
/// délais au service de paiement.
///
/// LES MÉTHODES TYPÉES NE REMPLACENT PAS <c>GetJsonAsync</c>, ELLES LE DOUBLENT.
///
/// L'héritage de <see cref="IServiceClient"/> est conservé : le mécanisme
/// configuré s'appuie dessus pour brancher une section dont le contrat n'est pas
/// stabilisé. Les méthodes ci-dessous sont la voie normale ; l'autre est
/// l'échappatoire.
/// </remarks>
public interface ICatalogClient : IServiceClient
{
    /// <summary>
    /// <c>GET /api/catalog/products/{id}</c> — route publique, anonyme.
    /// </summary>
    /// <remarks>
    /// Rend <c>IsNotFound</c> pour un produit inexistant : la distinction avec
    /// une panne est portée par le résultat, pas par une exception.
    /// </remarks>
    Task<ServiceResult<CatalogProduct>> GetProductAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><c>GET /api/catalog/categories</c> — route publique, anonyme.</summary>
    Task<ServiceResult<IReadOnlyList<CatalogCategory>>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/catalog/sellers/{sellerId}/products</c> — route publique.</summary>
    Task<ServiceResult<IReadOnlyList<CatalogProduct>>> ListSellerProductsAsync(
        Guid sellerId, CancellationToken cancellationToken);
}
