namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>
/// Fiche produit HBAExpress, taillée pour l'écran mobile (§38).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI A ÉTÉ RETIRÉ DU CONTRAT AMONT, ET POURQUOI.
///
/// `ProductSummary` porte `Slug`, `Gtin`, `Ean`, `ProductGroupId`, `Tags`,
/// `Attributes` et, sur chaque média, `Id` + `Position` + `Type`. Aucun de ces
/// champs n'est lu par la fiche produit mobile. Les transporter alourdirait
/// chaque réponse d'un tiers pour rien — sur un réseau où l'octet se paie.
///
/// `CategoryId` et `BrandId` sont conservés : ils servent à la navigation
/// (« voir la catégorie »), pas à l'affichage.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ProductDetailDto(
    ProductDetailProduct Product,
    IReadOnlyList<ProductDetailVariant> Variants,
    IReadOnlyList<ProductDetailMedia> Media,
    ProductDetailRating? Rating,
    ProductDetailStore? Store,
    ProductDetailDelivery Delivery);

public sealed record ProductDetailProduct(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Description,
    string Status);

/// <summary>
/// Une déclinaison et son stock.
/// </summary>
/// <param name="Available">
/// Quantité disponible, ou <c>null</c> si inventory-service n'a pas répondu.
///
/// `null` ET NON `0`. C'EST LA DISTINCTION LA PLUS IMPORTANTE DU FICHIER.
///
/// Zéro signifie « rupture » et fait disparaître le bouton d'achat. `null`
/// signifie « inconnu » et laisse le client afficher « disponibilité en cours de
/// vérification ». Les confondre ferait afficher « rupture » sur tout le
/// catalogue pendant une panne de stock — et perdre les ventes correspondantes.
/// </param>
public sealed record ProductDetailVariant(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    int? Available);

/// <summary>
/// UNE URL, JAMAIS DES OCTETS (§39).
///
/// `MediaId` accompagne l'URL pour permettre au client de demander plus tard une
/// variante (vignette, format écran) au service média.
/// </summary>
public sealed record ProductDetailMedia(Guid MediaId, string Url, bool IsPrimary, string AltText);

public sealed record ProductDetailRating(double Average, int Count);

public sealed record ProductDetailStore(Guid Id, string Name, string? LogoUrl, bool IsSelling);

/// <summary>
/// Estimation de livraison.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUN TARIF N'EST CALCULÉ ICI, ET AUCUN NE DOIT L'ÊTRE (§19).
///
/// Un devis de livraison dépend de l'adresse de destination — que la fiche
/// produit ne connaît pas — et d'un moteur tarifaire dont l'existence n'est pas
/// établie. `Available = false` et `Fee = null` disent honnêtement « pas
/// calculé ».
///
/// Le contre-exemple à ne jamais reproduire : afficher « livraison 1 000 F »
/// depuis une constante. Le client le lit comme un engagement, et l'écart se
/// découvre au paiement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ProductDetailDelivery(bool Available, int? Fee, int? EtaMinutes)
{
    public static ProductDetailDelivery NotEvaluated => new(false, null, null);
}
