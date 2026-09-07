namespace HBA.Catalog.Domain.Products;

/// <summary>Identité forte du produit (strongly-typed id).</summary>
public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
