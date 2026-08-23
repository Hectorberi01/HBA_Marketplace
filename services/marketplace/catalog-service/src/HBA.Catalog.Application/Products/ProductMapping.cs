using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products;

/// <summary>
/// Projection <see cref="Product"/> → <see cref="ProductSummary"/>.
///
/// Cette projection était recopiée à l'identique dans quatre fichiers
/// (GetProductQueryHandler, ListAllProducts, ListProductsBySeller, CatalogModuleApi).
/// Avec le cache, la redondance devient un piège : la fiche produit et l'API
/// inter-module partagent la MÊME clé Redis. Si leurs deux projections
/// divergeaient — un champ ajouté ici et pas là — la valeur servie dépendrait de
/// qui a rempli le cache en premier. Un bug non déterministe, qui n'apparaît qu'en
/// production. Une seule projection, et elle vit ici.
///
/// ═══════════════════════════════════════════════════════════════════════════════
/// IL FAUT DÉSORMAIS CHOISIR QUELLE RÉVISION ON PROJETTE.
///
/// C'est le point de tout le §6, et il se joue ici. Deux entrées, jamais une :
///
///   • <see cref="ToSellerSummary"/> rend ce que le VENDEUR édite — y compris un
///     brouillon que personne n'a validé ;
///   • <see cref="ToPublicSummary"/> rend ce que l'ACHETEUR doit voir, et REND
///     NULL tant que rien n'a été publié.
///
/// Une méthode unique aurait forcément choisi pour l'appelant, et elle aurait
/// choisi mal dans le sens le plus coûteux : servir au public un texte non relu.
/// C'est exactement ce que faisait l'ancienne version — sans le savoir, puisque
/// le produit n'avait qu'un seul jeu de champs.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ProductMapping
{
    /// <summary>La vue VENDEUR / ADMIN : la révision en cours d'édition.</summary>
    public static ProductSummary ToSellerSummary(Product product)
        => Projeter(product, product.CurrentRevision);

    /// <summary>
    /// La vue PUBLIQUE : la révision publiée, ou <c>null</c>.
    ///
    /// NULL N'EST PAS UNE ERREUR ICI — c'est le §17 : « Elle ne doit retourner
    /// que la révision publiée des produits PUBLISHED. » Un produit approuvé mais
    /// jamais publié n'a rien à montrer, et se rabattre sur la révision courante
    /// serait précisément la fuite que cette API doit empêcher.
    /// </summary>
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
        //
        // La fiche technique a un ordre voulu par le vendeur (§12) — c'est la seule
        // raison pour laquelle elle est stockée en deux tables plutôt qu'en jsonb.
        // La rendre non triée obligerait chaque client — web, Android, iOS, et les
        // intégrations — à retrouver la même règle ; il suffirait qu'un seul
        // l'oublie pour que la fiche s'affiche dans l'ordre de lecture de
        // PostgreSQL, c'est-à-dire arbitraire et changeant.
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
