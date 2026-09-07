namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>Fiche produit HBAExpress, taillée pour l'écran mobile (§38).</summary>
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

/// <summary>Une déclinaison et son stock.</summary>
/// <param name="Available">
/// Quantité disponible, ou <c> null</c> si inventory-service n'a pas répondu.
/// </param>
public sealed record ProductDetailVariant(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Attributes,
    int? Available);

/// <summary>UNE URL, JAMAIS DES OCTETS (§39).</summary>
public sealed record ProductDetailMedia(Guid MediaId, string Url, bool IsPrimary, string AltText);

public sealed record ProductDetailRating(double Average, int Count);

public sealed record ProductDetailStore(Guid Id, string Name, string? LogoUrl, bool IsSelling);

/// <summary>Estimation de livraison.</summary>
public sealed record ProductDetailDelivery(bool Available, int? Fee, int? EtaMinutes)
{
    public static ProductDetailDelivery NotEvaluated => new(false, null, null);
}
