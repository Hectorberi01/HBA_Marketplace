using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Queries.PublicCatalog;

// ═════════════════════════════════════════════════════════════════════════════
// LA VITRINE (§17) — LES SEULES REQUÊTES QU'UNE ROUTE ANONYME PEUT APPELER.
//
// CE FICHIER EXISTE PARCE QUE LE PARTAGE A ÉCHOUÉ.
//
// Les trois routes publiques du catalogue se branchaient sur les requêtes du
// back-office : `ListAllProductsQuery` (documentée « console admin »),
// `GetProductQuery` et `ListProductsBySellerQuery`. Toutes trois projettent
// `ToSellerSummary`, c'est-à-dire la révision COURANTE.
//
// Le résultat était qu'un visiteur anonyme voyait les brouillons, les fiches en
// attente de validation, les rejetées, les suspendues — et, pour une fiche
// publiée, la version en cours de relecture plutôt que celle qui avait été
// approuvée. Le §17 dit l'inverse en une phrase : « Elle ne doit retourner que la
// révision publiée des produits PUBLISHED. »
//
// La leçon n'est pas « il fallait ajouter un filtre ». Une requête qui sert deux
// publics finit par servir le mauvais : il suffit qu'un paramètre facultatif ne
// soit pas passé. Les requêtes de vitrine sont donc SÉPARÉES, et elles n'ont
// aucun paramètre capable d'élargir ce qu'elles montrent.
//
// AUCUNE DE CES TROIS REQUÊTES N'EST MISE EN CACHE, ET C'EST PROVISOIRE.
//
// Le chemin qu'elles remplacent l'était. Le remettre demande des clés PROPRES :
// partager la clé de la vue vendeur — ce que faisaient `GetProductQuery` et
// `CatalogModuleApi` — rouvrirait la fuite d'une autre manière, puisque celui qui
// remplit le cache en premier déciderait de ce que voit le public. Un cache mal
// clé ici ne se verrait pas en développement, où il est vide.
//
// La correction d'abord, l'optimisation ensuite, avec son encadré d'invalidation.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Recherche de vitrine (§17 : query, categoryId, brandId, sellerId, condition, prix, tri, pagination).</summary>
public sealed record SearchPublicProductsQuery(
    string? Query = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? SellerId = null,
    string? Condition = null,
    long? MinPrice = null,
    long? MaxPrice = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize) : IQuery<PagedResult<ProductSummary>>;

internal sealed class SearchPublicProductsQueryHandler
    : IQueryHandler<SearchPublicProductsQuery, PagedResult<ProductSummary>>
{
    private readonly IProductRepository _products;

    public SearchPublicProductsQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<PagedResult<ProductSummary>>> Handle(
        SearchPublicProductsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // UN ÉTAT COMMERCIAL INCONNU EST IGNORÉ, PAS REFUSÉ.
        //
        // C'est l'inverse du choix fait à l'écriture (`ContenuProduitFactory`
        // refuse « REFURBISHD »). Ici la valeur vient d'une URL publique, souvent
        // d'un lien partagé ou d'un robot : rendre 400 sur un filtre mal orthographié
        // casserait la page entière là où l'ignorer rend simplement plus de
        // résultats. À l'écriture, une faute de frappe devient une promesse
        // commerciale ; à la lecture, elle ne coûte rien.
        ProductConditionType? condition = null;
        if (!string.IsNullOrWhiteSpace(query.Condition)
            && Enum.TryParse<ProductConditionType>(
                query.Condition.Replace("_", string.Empty), ignoreCase: true, out var analysee)
            && Enum.IsDefined(typeof(ProductConditionType), analysee))
        {
            condition = analysee;
        }

        var (items, total) = await _products.SearchPublishedAsync(
            new RecherchePublique(
                query.Query,
                query.CategoryId,
                query.BrandId,
                query.SellerId,
                condition,
                query.MinPrice,
                query.MaxPrice,
                query.Sort,
                page,
                pageSize),
            cancellationToken);

        var resumes = items
            .Select(ProductMapping.ToPublicSummary)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

        // PAS DE FACETTES ICI.
        //
        // `ListAllProductsQuery` rend la répartition du catalogue PAR STATUT — donc
        // le nombre de brouillons, de fiches rejetées et de suspendues. C'est une
        // information de gouvernance, et elle sortait telle quelle sur la route
        // anonyme.
        return Result.Success(new PagedResult<ProductSummary>(resumes, total, page, pageSize));
    }
}

/// <summary>Fiche publique par slug (§17 : <c>GET /products/{slug}</c>).</summary>
public sealed record GetPublicProductBySlugQuery(string Slug) : IQuery<ProductSummary>;

internal sealed class GetPublicProductBySlugQueryHandler
    : IQueryHandler<GetPublicProductBySlugQuery, ProductSummary>
{
    private readonly IProductRepository _products;

    public GetPublicProductBySlugQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<ProductSummary>> Handle(
        GetPublicProductBySlugQuery query, CancellationToken cancellationToken)
    {
        var product = await _products.GetPublishedBySlugAsync(query.Slug, cancellationToken);
        var resume = product is null ? null : ProductMapping.ToPublicSummary(product);

        if (resume is null)
        {
            return Error.NotFound("catalog.product.not_found", "Ce produit n'existe pas ou n'est plus en vente.");
        }

        return resume;
    }
}

/// <summary>
/// Fiche publique par identifiant.
///
/// CONSERVÉE ALORS QUE LE §17 NE DEMANDE QUE LE SLUG.
///
/// L'application mobile appelle déjà `/api/catalog/products/{id}`, et les liens
/// profonds déjà émis portent des identifiants. La retirer casserait les
/// installations existantes pour un gain de conformité nul. Elle rend exactement
/// la même chose que la route par slug — même projection, même filtre.
/// </summary>
public sealed record GetPublicProductQuery(Guid ProductId) : IQuery<ProductSummary>;

internal sealed class GetPublicProductQueryHandler
    : IQueryHandler<GetPublicProductQuery, ProductSummary>
{
    private readonly IProductRepository _products;

    public GetPublicProductQueryHandler(IProductRepository products) => _products = products;

    public async Task<Result<ProductSummary>> Handle(
        GetPublicProductQuery query, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(new ProductId(query.ProductId), cancellationToken);
        var resume = product is null ? null : ProductMapping.ToPublicSummary(product);

        // MÊME MESSAGE POUR « N'EXISTE PAS » ET « PAS PUBLIÉ ».
        //
        // Les distinguer dirait à un visiteur anonyme qu'une fiche existe mais
        // n'est pas en ligne — et permettrait, en balayant des identifiants, de
        // dénombrer les brouillons d'un concurrent. Un 404 uniforme ne dit rien.
        if (resume is null)
        {
            return Error.NotFound("catalog.product.not_found", "Ce produit n'existe pas ou n'est plus en vente.");
        }

        return resume;
    }
}
