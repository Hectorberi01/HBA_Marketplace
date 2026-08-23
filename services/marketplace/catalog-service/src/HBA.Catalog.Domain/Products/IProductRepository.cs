namespace HBA.Catalog.Domain.Products;

/// <summary>
/// Port de persistance du produit, défini dans le Domain et implémenté en
/// Infrastructure (inversion de dépendance). L'Application dépend de cette
/// abstraction, jamais d'EF Core.
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
    /// (fermeture de compte : dépublication ; suppression : archivage). Contrairement
    /// à <see cref="ListBySellerAsync"/> (AsNoTracking), les entités renvoyées sont
    /// suivies par le contexte, donc leurs changements sont persistés au SaveChanges.
    /// </summary>
    Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les produits de la plateforme (gouvernance admin).</summary>
    Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de produits pour la console admin : recherche par nom, filtre par statut,
    /// tri par date de création décroissante. Renvoie le total filtré + la répartition
    /// par statut (calculée avant le filtre statut).
    /// </summary>
    Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parmi les slugs proposés, ceux qu'une révision PUBLIÉE occupe déjà.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ELLE EXISTE PARCE QUE CHERCHER UN SLUG LIBRE COÛTAIT JUSQU'À CENT
    ///     REQUÊTES PAR CRÉATION DE PRODUIT (§11).
    ///
    /// La recherche testait « base », puis « base-2 », « base-3 »… jusqu'à
    /// « base-100 », un aller-retour par tentative. Sur un catalogue où les
    /// homonymes sont la norme — « robe rouge », « chargeur USB » — un vendeur qui
    /// publie sa centième variante déclenchait cent requêtes pour choisir un
    /// suffixe.
    ///
    /// ET LA BOUCLE ÉTAIT ÉCRITE DEUX FOIS, dans `CreateProduct` et dans
    /// `CreateProductWithImages`. Corriger l'une aurait laissé l'autre.
    ///
    /// ON DEMANDE « LESQUELS SONT PRIS », PAS « DONNE-MOI LES SLUGS QUI
    ///     COMMENCENT PAR ». `Slug` est un objet-valeur adossé à un convertisseur :
    /// EF sait traduire une égalité ou un `Contains` sur la liste convertie, il ne
    /// sait PAS traduire un `StartsWith` sur la chaîne sous-jacente. Passer les
    /// candidats en clair est donc la seule forme qui tienne — et elle a l'avantage
    /// d'être exacte au lieu d'être approchée par un motif.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<IReadOnlyCollection<Slug>> ListTakenSlugsAsync(
        IReadOnlyCollection<Slug> candidats, CancellationToken cancellationToken = default);

    // ═════════════════════════════════════════════════════════════════════════
    // LA VITRINE (§17) — DEUX MÉTHODES QUI NE RENDENT QUE DU PUBLIÉ.
    //
    // ELLES SONT SÉPARÉES DES AUTRES POUR QU'ON NE PUISSE PAS SE TROMPER.
    //
    // La fuite qu'elles ferment venait d'un partage : la route anonyme appelait
    // `ListPagedAsync`, écrite pour la console d'administration, avec un filtre de
    // statut FACULTATIF. Sans paramètre, la vitrine servait les brouillons, les
    // fiches en attente de validation et les suspendues.
    //
    // Une méthode qui sert deux publics finit par servir le mauvais. Celles-ci
    // n'ont pas de paramètre de statut à oublier.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Le produit dont la RÉVISION PUBLIÉE porte ce slug, et dont le produit est
    /// lui-même publié.
    ///
    /// LES DEUX CONDITIONS SONT NÉCESSAIRES.
    ///
    /// Dépublier ne change PAS le statut de la révision : elle reste `Published`,
    /// elle cesse simplement d'être servie. C'est voulu — cela réserve l'URL et
    /// permet de republier sans nouvelle validation. Mais chercher sur la seule
    /// révision rendrait donc les fiches dépubliées, ce qui est exactement ce que
    /// le vendeur venait de refuser.
    /// </summary>
    Task<Product?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Recherche de vitrine. Ne rend que des produits <c>Published</c>.</summary>
    Task<(IReadOnlyList<Product> Items, int Total)> SearchPublishedAsync(
        RecherchePublique criteres, CancellationToken cancellationToken = default);

    /// <summary>
    /// La file de validation du §16 : les fiches dont la RÉVISION COURANTE attend
    /// une décision.
    ///
    /// LE CRITÈRE PORTE SUR LA RÉVISION, PAS SUR LE STATUT DU PRODUIT.
    ///
    /// Un produit déjà en vente dont la nouvelle version est soumise reste
    /// `Published` (§6) — c'est tout l'objet des révisions. Filtrer sur
    /// `Status == PendingReview` ferait donc disparaître de la file exactement les
    /// fiches les plus urgentes à relire : celles qui sont déjà devant des
    /// acheteurs.
    ///
    /// Les plus anciennes soumissions d'abord : une file de modération triée à
    /// l'envers laisse le premier arrivé attendre indéfiniment.
    /// </summary>
    Task<(IReadOnlyList<Product> Items, int Total)> ListPendingReviewAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les SKU de variantes données, indexés par identifiant de variante.
    /// </summary>
    /// <remarks>
    /// AJOUTÉE POUR LES OFFRES, ET SANS FILTRE PAR VENDEUR.
    ///
    /// Quand une boutique rouvre, ses offres redeviennent achetables — mais ce
    /// module ne connaît pas le stock. Il rend donc le SKU de chaque offre
    /// relevée, pour que l'appelant interroge Inventory et remette en rupture ce
    /// qui doit l'être.
    ///
    /// Aucun filtre par vendeur : une offre porte souvent sur le produit d'un
    /// tiers. En ajouter un ferait disparaître ces SKU du résultat, et les offres
    /// concernées repartiraient en vente sans que leur stock ait été vérifié.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, string>> GetSkusByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default);

    /// <summary>Les noms de produits donnés, indexés par identifiant.</summary>
    /// <remarks>
    /// POUR L'ÉCRAN « MISES EN VENTE », ET EN LOT.
    ///
    /// Une offre ne porte pas le nom du produit : elle le référence par
    /// identifiant. Or la liste des offres d'un vendeur affiche « Poulet braisé —
    /// 5 500 F », pas un GUID. Charger la fiche complète de chaque offre pour un
    /// libellé serait un N+1 sur l'agrégat le plus lourd du service.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}
