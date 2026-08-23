namespace HBA.Merchants.Application;

/// <summary>
/// Clés de cache du module Sellers.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE MODULE A BESOIN D'UN CACHE, ALORS QU'IL N'EST PAS « CHAUD »
///
/// Il l'est, mais indirectement — et c'est ce qui rendait le problème invisible.
///
/// La fiche produit mobile agrège les offres du produit, puis résout le nom de
/// boutique de CHAQUE vendeur, un par un, dans une boucle (voir
/// MobileCatalogEndpoints.ProductDetailAsync). Chaque tour de boucle était une
/// requête SQL — et chacune chargeait le vendeur AVEC tous ses documents KYB, pour
/// n'en retenir que le nom de la boutique.
///
/// Un produit vendu par trois marchands, c'était donc trois requêtes lourdes, à
/// chaque affichage de fiche, pour trois chaînes de caractères qui ne changent
/// jamais.
///
/// C'est le N+1 le plus coûteux de l'application, et il ne se voyait dans aucun
/// endpoint : il se cachait dans une boucle.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class SellersCacheKeys
{
    /// <summary>Résumé d'un vendeur, par identifiant de vendeur.</summary>
    public static string Seller(Guid sellerId) => $"sellers:seller:{sellerId}";

    /// <summary>Résumé d'un vendeur, par identifiant d'utilisateur (connexion vendeur).</summary>
    public static string SellerByUser(Guid userId) => $"sellers:by-user:{userId}";

    /// <summary>
    /// 10 minutes.
    ///
    /// Un nom de boutique, un logo, un statut : ça ne bouge quasiment jamais, et
    /// toute écriture invalide la clé sur-le-champ. Ce TTL n'est qu'un filet.
    /// </summary>
    public static readonly TimeSpan SellerTtl = TimeSpan.FromMinutes(10);

    /// <summary>Mémorisation d'une absence — protège les endpoints anonymes.</summary>
    public static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Contexte d'autorisation d'un compte : son vendeur, ses permissions.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE CACHE VIT À UN SEUL ENDROIT, ET C'EST CE QUI REND LA COUPURE VRAIE.
    ///
    /// Le cahier (§50) le voulait chez chaque appelant, invalidé par un événement
    /// Kafka. Cinq services, cinq copies, cinq occasions d'en oublier une — et
    /// dans un groupe de consommateurs, une SEULE instance reçoit le message.
    ///
    /// Ici l'entrée vit chez seller-service, et elle est évincée dans le MÊME
    /// `SaveChangesAsync` que la mutation qui la périme (voir
    /// `SellersDbContext.CollectCacheKeysToEvict`). Depuis que Redis est
    /// réellement branché, cette éviction est globale : la requête suivante, sur
    /// n'importe quelle réplique de n'importe quel service, voit l'état à jour.
    ///
    /// C'est la garantie du §53 — « après suspension, toutes les répliques
    /// refusent sans attendre le TTL » — obtenue sans aucun aller-retour Kafka.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static string MemberAccess(Guid userId) => $"sellers:access:{userId}";

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE VENDEUR A-T-IL LE DROIT D'OPÉRER ?
    ///
    /// CLÉ SÉPARÉE DE `MemberAccess`, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// On aurait pu ranger le statut du vendeur DANS le contexte d'accès mis en
    /// cache par utilisateur. Ç'aurait été plus simple à lire, et faux à
    /// l'invalidation : quand un administrateur suspend un vendeur, seule
    /// l'entité `Seller` est modifiée — aucun `SellerMember` n'est suivi. La
    /// boucle d'éviction n'aurait donc évincé la clé d'AUCUN membre, et la
    /// suspension aurait mis deux minutes à mordre pour toute l'équipe.
    ///
    /// Portée par le VENDEUR, la clé tombe avec lui : la boucle
    /// `ChangeTracker.Entries&lt;Seller&gt;()` la connaît, et une suspension prend
    /// effet à la requête suivante, sur n'importe quelle réplique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static string SellerCanOperate(Guid sellerId) => $"sellers:can-operate:{sellerId}";

    /// <summary>
    /// Deux minutes.
    /// </summary>
    /// <remarks>
    /// BEAUCOUP PLUS COURT QUE `SellerTtl`, ET POUR UNE RAISON PRÉCISE.
    ///
    /// Les mutations d'APPARTENANCE évincent cette clé sur-le-champ : suspension,
    /// révocation, changement de rôles. Mais une modification des permissions d'un
    /// RÔLE périme l'accès de tous ceux qui le portent, et le contexte de
    /// persistance ne sait pas les énumérer sans une requête que personne ne veut
    /// payer à chaque écriture.
    ///
    /// Ce TTL est donc le délai maximal de propagation d'un changement de RÔLE —
    /// jamais celui d'une révocation, qui est immédiate. Deux minutes : assez
    /// court pour qu'un droit retiré d'un rôle ne survive pas à une pause café,
    /// assez long pour que l'autorisation ne redevienne pas une requête SQL par
    /// appel de la plateforme.
    /// </remarks>
    public static readonly TimeSpan MemberAccessTtl = TimeSpan.FromMinutes(2);
}
