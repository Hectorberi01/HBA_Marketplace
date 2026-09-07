namespace HBA.Catalog.Application;

/// <summary>Clés de cache du catalogue (cache-aside via ICacheService / Redis).</summary>
public static class CatalogCacheKeys
{
    /// <summary>Fiche produit. Chemin le plus chaud de toute l'application (et anonyme).</summary>
    public static string Product(Guid productId) => $"catalog:product:{productId}";

    /// <summary>Produits d'une boutique (back-office vendeur, et page boutique).</summary>
    public static string ProductsBySeller(Guid sellerId) => $"catalog:products:seller:{sellerId}";

    /// <summary>Catégorie unitaire.</summary>
    public static string Category(Guid categoryId) => $"catalog:category:{categoryId}";

    /// <summary>Arbre des catégories. Lu à chaque ouverture de l'application.</summary>
    public const string AllCategories = "catalog:categories:all";

    /// <summary>Marque unitaire.</summary>
    public static string Brand(Guid brandId) => $"catalog:brand:{brandId}";

    /// <summary>Toutes les marques.</summary>
    public const string AllBrands = "catalog:brands:all";

    /// <summary>Produit : 5 minutes.</summary>
    public static readonly TimeSpan ProductTtl = TimeSpan.FromMinutes(5);

    /// <summary>Catégories et marques : 30 minutes.</summary>
    public static readonly TimeSpan ReferenceDataTtl = TimeSpan.FromMinutes(30);

    /// <summary>Liste des produits d'une boutique : 2 minutes.</summary>
    public static readonly TimeSpan SellerProductsTtl = TimeSpan.FromMinutes(2);

    /// <summary>Durée de mémorisation d'une ABSENCE (produit inexistant) : 30 secondes.</summary>
    public static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);
}
