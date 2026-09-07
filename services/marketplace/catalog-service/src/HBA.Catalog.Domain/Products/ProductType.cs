namespace HBA.Catalog.Domain.Products;

/// <summary>Nature du produit (§8 : "productType": "PHYSICAL").</summary>
public enum ProductType
{
    /// <summary>Bien matériel : stock, poids, dimensions, livraison.</summary>
    Physical = 0,

    /// <summary>Bien immatériel livré par téléchargement ou par lien.</summary>
    Digital = 1,

    /// <summary>Prestation : ni stock ni livraison, mais une exécution.</summary>
    Service = 2
}
