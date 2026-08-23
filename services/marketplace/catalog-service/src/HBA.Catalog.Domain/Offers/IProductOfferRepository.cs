namespace HBA.Catalog.Domain.Offers;

/// <summary>
/// Accès aux offres. Agrégat séparé — voir l'encadré de <see cref="ProductOffer"/>.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// FICHIER À PART, ALORS QUE LE MONOLITHE LE LOGEAIT DANS `IProductRepository.cs`.
///
/// Là-bas, `IProductRepository` et `IProductOfferRepository` cohabitaient dans le
/// même fichier — deux agrégats, deux cycles de vie, une seule porte d'entrée.
/// C'est précisément ce qui a fait croire, pendant toute la préparation de cette
/// phase, que produit et offre étaient inséparables et qu'il fallait extraire les
/// trente-trois fichiers d'un bloc.
///
/// Ils ne le sont pas : l'offre référence le produit PAR IDENTIFIANT, et aucune
/// des huit méthodes ci-dessous ne touche l'agrégat `Product`. Le fichier séparé
/// rend cette frontière visible depuis l'arborescence, et non seulement depuis la
/// ligne 56 d'un fichier qui parle d'autre chose.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IProductOfferRepository
{
    Task<ProductOffer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default);

    /// <summary>Offres achetables d'un produit — c'est la Buy Box.</summary>
    Task<IReadOnlyList<ProductOffer>> ListActiveByProductAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les offres non archivées d'une boutique, pour AFFICHAGE, dans la limite de
    /// <paramref name="take"/>.
    /// </summary>
    /// <remarks>
    /// C'EST LA LECTURE, ET ELLE SEULE, QUI EST BORNÉE. Ses deux voisines
    /// `ListAll…ForUpdateAsync` ne le sont pas et ne doivent pas l'être : elles
    /// servent la suspension d'un vendeur et la fermeture d'une boutique, où rendre
    /// une partie des offres laisserait le reste en vente. Voir leurs encadrés.
    /// </remarks>
    Task<IReadOnlyList<ProductOffer>> ListByStoreAsync(
        Guid storeId, int take = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toutes les offres portant sur une variante donnée.
    ///
    /// SERT À UNE RÈGLE, PAS À UN ÉCRAN : quand une variante est désactivée,
    /// ses offres doivent être archivées. Sans cette lecture, un acheteur
    /// commanderait une déclinaison qui n'est plus proposée, et le vendeur
    /// découvrirait la commande sans savoir quoi expédier.
    /// </summary>
    Task<IReadOnlyList<ProductOffer>> ListByVariantAsync(
        Guid variantId, CancellationToken cancellationToken = default);

    /// <summary>Une boutique ne propose qu'une offre par variante.</summary>
    Task<bool> ExistsForStoreAndVariantAsync(
        Guid storeId, Guid variantId, CancellationToken cancellationToken = default);

    Task AddAsync(ProductOffer offer, CancellationToken cancellationToken = default);

    /// <summary>
    /// TOUTES les offres d'un vendeur, sans pagination et SUIVIES par EF.
    ///
    /// PAR VENDEUR, PAS PAR BOUTIQUE. <see cref="ListByStoreAsync"/> donnerait
    /// aujourd'hui le même résultat, puisque `StoreId` est peuplé avec
    /// l'identifiant du vendeur — mais c'est un accident de la reprise, pas une
    /// règle. Le jour où `Store` existera vraiment, s'appuyer dessus ne
    /// suspendrait qu'une des boutiques d'un vendeur sanctionné.
    ///
    /// CE JOUR EST DÉJÀ ARRIVÉ CÔTÉ HBA, et c'est à vérifier en 3.2 :
    /// merchant-service porte `Store` depuis la tâche S6, et `ISellerModuleApi`
    /// expose `ListStoresBySellerAsync`. L'accident de reprise décrit ci-dessus
    /// peut donc ne plus en être un — les données amorcées diront lequel des deux
    /// identifiants `StoreId` contient réellement.
    /// </summary>
    Task<IReadOnlyList<ProductOffer>> ListAllBySellerForUpdateAsync(
        Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// TOUTES les offres d'une BOUTIQUE, sans pagination et SUIVIES par EF.
    ///
    /// NE PAS CONFONDRE AVEC <see cref="ListByStoreAsync"/>, qui sert des
    /// écrans : celle-ci est détachée et filtre les archivées. Ici on applique une
    /// décision — une boutique ferme — et il ne faut en oublier aucune.
    /// </summary>
    Task<IReadOnlyList<ProductOffer>> ListAllByStoreForUpdateAsync(
        Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Plusieurs offres par identifiant, en une requête.</summary>
    /// <remarks>
    /// EXISTE POUR LE PANIER, ET C'EST UNE QUESTION DE N+1.
    ///
    /// `GetOffersAsync` du contrat public reçoit une COLLECTION d'identifiants :
    /// un panier de huit articles, c'est huit offres. Les lire une par une ferait
    /// huit allers-retours à chaque affichage du panier — sur une connexion
    /// mobile béninoise, c'est la différence entre un écran et une attente.
    /// </remarks>
    Task<IReadOnlyList<ProductOffer>> ListByIdsAsync(
        IReadOnlyCollection<OfferId> ids, CancellationToken cancellationToken = default);

    /// <summary>Les offres qui vendent une référence d'inventaire donnée.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE OFFRE NE PORTE PAS DE SKU. C'EST UNE JOINTURE, PAS UN FILTRE.
    ///
    /// `ProductOffer` référence une VARIANTE ; c'est la variante qui porte le
    /// SKU. Retrouver les offres d'une référence exige donc de passer par
    /// `Product.Variants` — et c'est la seule lecture d'offres qui traverse
    /// l'agrégat produit.
    ///
    /// RENVOIE UNE LISTE, ET LE CONTRAT PUBLIC LE DIT DÉJÀ : le SKU n'est
    /// unique qu'au sein d'un produit. Deux produits distincts peuvent porter le
    /// même. Inventory s'en sert pour signaler une rupture : n'en rendre qu'une
    /// en marquerait une sur deux.
    ///
    /// LES ARCHIVÉES SONT EXCLUES. Une offre retirée définitivement ne doit
    /// pas repasser en rupture — son état est terminal.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<IReadOnlyList<ProductOffer>> ListBySkuAsync(
        string sku, CancellationToken cancellationToken = default);
}
