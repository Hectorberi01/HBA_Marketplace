using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products;

/// <summary>Projection <see cref="Product"/> → <see cref="ProductSummary"/>.</summary>
public static class ProductMapping
{
    /// <summary>La vue VENDEUR / ADMIN : la révision en cours d'édition.</summary>
    public static ProductSummary ToSellerSummary(Product product)
        => Projeter(product, product.CurrentRevision);

    /// <summary>La vue PUBLIQUE : la révision publiée, ou <c>null</c>.</summary>
    public static ProductSummary? ToPublicSummary(Product product)
    {
        if (!ProductStatusTransitions.IsPubliclyVisible(product.Status))
        {
            return null;
        }

        var publiee = product.PublishedRevision;
        return publiee is null ? null : Projeter(product, publiee);
    }

    private static ProductSummary Projeter(Product product, ProductRevision revision)
    {
        var variants = product.Variants
            .Select(v => new ProductVariantSummary(
                v.Id,
                v.Sku.Value,
                v.VariantAttributes,
                v.Barcode,
                v.WeightGrams))
            .ToList();

        var media = product.Media
            .OrderBy(m => m.Position)
            .Select(m => new ProductMediaSummary(
                m.Id,
                m.MediaId,
                m.Url,
                m.Type.ToString(),
                m.IsPrimary,
                m.Position,
                m.AltText))
            .ToList();

        // LE TRI SE FAIT ICI, PAS CHEZ LE CLIENT.
        var specifications = revision.Specifications
            .OrderBy(g => g.DisplayOrder)
            .Select(g => new ProductSpecificationGroupSummary(
                g.Id,
                g.Name,
                g.DisplayOrder,
                g.Items
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => new ProductSpecificationSummary(i.Id, i.Name, i.Value, i.DisplayOrder))
                    .ToList()))
            .ToList();

        return new ProductSummary(
            product.Id.Value,
            product.SellerId,
            revision.CategoryId,
            revision.BrandId,
            revision.Name,
            revision.Description,
            revision.Slug.Value,
            product.Status.ToString(),
            product.Gtin,
            product.Ean,
            product.ProductGroupId,
            revision.Attributes,
            revision.Tags.ToList(),
            variants,
            media,
            specifications,
            product.StoreId);
    }
}
