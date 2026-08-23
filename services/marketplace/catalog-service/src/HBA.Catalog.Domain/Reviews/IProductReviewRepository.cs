using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Domain.Reviews;

/// <summary>
/// Port de persistance des décisions d'administration.
///
/// LA FILE D'ATTENTE NE SE LIT PAS DANS CETTE TABLE.
///
/// `product_reviews` est un journal de décisions RENDUES. Ce que le §16 appelle
/// « file de validation » — <c>GET /products/reviews</c> — est l'ensemble des
/// fiches EN ATTENTE, c'est-à-dire celles dont la révision courante est
/// `PendingReview`. Elles n'ont, par définition, aucune décision.
///
/// Confondre les deux donnerait une file toujours vide, et personne ne
/// comprendrait pourquoi les soumissions n'y arrivent pas. La file passe donc par
/// <see cref="IProductRepository"/> ; ce port-ci sert l'historique.
/// </summary>
public interface IProductReviewRepository
{
    Task AddAsync(ProductReview review, CancellationToken cancellationToken = default);

    /// <summary>Les décisions rendues sur un produit, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<ProductReview>> ListByProductAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>La dernière décision rendue sur un produit, s'il y en a une.</summary>
    Task<ProductReview?> GetLatestForProductAsync(
        Guid productId, CancellationToken cancellationToken = default);
}
