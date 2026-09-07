namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>
/// Accueil HBAExpress.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN RESTAURANT NE FIGURE ICI, ET AUCUN NE DOIT Y FIGURER (§6, §45).
///
/// La séparation des deux univers est une exigence produit. Elle ne tient que si
/// elle est visible dans le TYPE : un champ `restaurants` ajouté « juste pour le
/// hub » ferait entrer HBA Food dans l'accueil marketplace, et la frontière
/// disparaîtrait d'abord du code, puis de l'écran.
///
/// La vue qui agrège les deux univers est l'accueil GLOBAL, et elle a son propre
/// type.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="FlashOffers">
/// TOUJOURS VIDE — AUCUN SERVICE NE LES PRODUIT (§51).
///
/// Le cahier des charges prévoit des offres flash, mais catalog-service
/// n'expose ni promotion, ni fenêtre temporelle, ni mise en avant. Le champ
/// existe pour que le contrat client soit stable le jour où l'endpoint
/// arrivera ; il n'est alimenté par rien aujourd'hui.
///
/// Il n'émet PAS d'avertissement : une absence permanente n'est pas une
/// dégradation, et le tableau d'avertissements ne doit signaler que ce qui
/// peut se rétablir.
/// </param>
/// <param name="RecentlyViewed">
/// TOUJOURS VIDE — AUCUN SERVICE NE MÉMORISE LES CONSULTATIONS.
///
/// « Vus récemment » suppose un historique de navigation que ni Engagement ni
/// Catalog ne tiennent. Le construire côté passerelle ferait de celle-ci le
/// propriétaire d'une donnée — ce que le §3 lui interdit.
/// </param>
public sealed record ExpressHomeDto(
    IReadOnlyList<ExpressCategory> Categories,
    IReadOnlyList<ExpressProductCard> RecommendedProducts,
    ExpressActiveOrder? ActiveOrder,
    IReadOnlyList<ExpressProductCard> FlashOffers,
    IReadOnlyList<ExpressProductCard> RecentlyViewed);

public sealed record ExpressCategory(Guid Id, string Name, string Slug, string? ImageUrl);

/// <summary>
/// Vignette produit d'une liste.
/// </summary>
/// <remarks>
/// PAS DE PRIX, ET C'EST UN MANQUE RÉEL À SIGNALER.
///
/// `ProductSummary` n'en porte aucun : le prix vit sur l'OFFRE, dans un module
/// que la passerelle n'atteint pas encore. Afficher une vignette sans prix est
/// mauvais ; en inventer un serait pire.
///
/// Manque à combler : une route rendant l'offre active d'un produit — ou un prix
/// projeté dans `ProductSummary`.
/// </remarks>
public sealed record ExpressProductCard(Guid Id, string Name, string? ImageUrl);

public sealed record ExpressActiveOrder(Guid Id, string Status, decimal GrandTotal, string Currency);
