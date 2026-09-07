using HBA.Shared.Domain.Primitives;

namespace HBA.Catalog.Domain.Products;

/// <summary>Déclinaison d'un produit (couleur, taille).</summary>
public sealed class ProductVariant : Entity<Guid>
{
    private ProductVariant()
    {
    }

    internal ProductVariant(
        Guid id,
        Sku sku,
        Dictionary<string, string> variantAttributes,
        string? barcode,
        int weightGrams,
        Dimensions? dimensions)
        : base(id)
    {
        Sku = sku;
        VariantAttributes = variantAttributes;
        Barcode = barcode;
        WeightGrams = weightGrams;
        Dimensions = dimensions;
    }

    public Sku Sku { get; private set; } = default!;

    /// <summary>Attributs dynamiques (couleur, taille…), mappés en jsonb.</summary>
    public Dictionary<string, string> VariantAttributes { get; private set; } = new();

    public string? Barcode { get; private set; }
    public int WeightGrams { get; private set; }

    /// <summary>La déclinaison est-elle proposable à la vente ?</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Retire la déclinaison de la vente.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>La remet en vente. NE RÉTABLIT AUCUNE OFFRE ARCHIVÉE.</summary>
    public void Reactivate() => IsActive = true;
    public Dimensions? Dimensions { get; private set; }

    internal void Update(Sku sku, Dictionary<string, string> variantAttributes, string? barcode, int weightGrams)
    {
        Sku = sku;
        VariantAttributes = variantAttributes;
        Barcode = barcode;
        WeightGrams = weightGrams;
    }
}
