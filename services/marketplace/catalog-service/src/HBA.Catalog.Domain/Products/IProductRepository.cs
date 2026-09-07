namespace HBA.Catalog.Domain.Products;

/// <summary>
/// Port de persistance du produit, défini dans le Domain et implémenté en
/// Infrastructure (inversion de dépendance).
/// </summary>
public interface IProductRepository
{
    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    void Remove(Product product);

    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>Liste les produits d'un vendeur (back-office vendeur).</summary>
    Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liste TRACKÉE des produits d'un vendeur, destinée à une mutation en masse
    /// (fermeture de compte : dépublication ; suppression : archivage).
    /// </summary>
    Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les produits de la plateforme (gouvernance admin).</summary>
    Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de produits pour la console admin : recherche par nom, filtre par
    /// statut, tri par date de création décroissante.
    /// </summary>
    Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Parmi les slugs proposés, ceux qu'une révision PUBLIÉE occupe déjà.</summary>
    Task<IReadOnlyCollection<Slug>> ListTakenSlugsAsync(
        IReadOnlyCollection<Slug> candidats, CancellationToken cancellationToken = default);

    // LA VITRINE (§17) — DEUX MÉTHODES QUI NE RENDENT QUE DU PUBLIÉ.

    /// <summary>
    /// Le produit dont la RÉVISION PUBLIÉE porte ce slug, et dont le produit est
    /// lui-même publié.
    /// </summary>
    Task<Product?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Recherche de vitrine. Ne rend que des produits <c>Published</c>.</summary>
    Task<(IReadOnlyList<Product> Items, int Total)> SearchPublishedAsync(
        RecherchePublique criteres, CancellationToken cancellationToken = default);

    /// <summary>
    /// La file de validation du §16 : les fiches dont la RÉVISION COURANTE attend
    /// une décision.
    /// </summary>
    Task<(IReadOnlyList<Product> Items, int Total)> ListPendingReviewAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Les SKU de variantes données, indexés par identifiant de variante.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetSkusByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Les noms de produits donnés, indexés par identifiant.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}
