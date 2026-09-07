namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>Accueil HBAExpress.</summary>
/// <param name="FlashOffers">TOUJOURS VIDE — AUCUN SERVICE NE LES PRODUIT (§51).</param>
/// <param name="RecentlyViewed">
/// TOUJOURS VIDE — AUCUN SERVICE NE MÉMORISE LES CONSULTATIONS.
/// </param>
public sealed record ExpressHomeDto(
    IReadOnlyList<ExpressCategory> Categories,
    IReadOnlyList<ExpressProductCard> RecommendedProducts,
    ExpressActiveOrder? ActiveOrder,
    IReadOnlyList<ExpressProductCard> FlashOffers,
    IReadOnlyList<ExpressProductCard> RecentlyViewed);

public sealed record ExpressCategory(Guid Id, string Name, string Slug, string? ImageUrl);

/// <summary>Vignette produit d'une liste.</summary>
public sealed record ExpressProductCard(Guid Id, string Name, string? ImageUrl);

public sealed record ExpressActiveOrder(Guid Id, string Status, decimal GrandTotal, string Currency);
