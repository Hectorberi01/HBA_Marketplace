using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using CatalogContracts = HBA.Catalog.Contracts;
using Contracts = HBA.Products.Contracts;
using Proto = HBA.Catalog.Grpc.V1;

namespace HBA.Products.Contracts.Grpc;

public sealed class CatalogGrpcService : Proto.CatalogApi.CatalogApiBase
{
    private readonly CatalogContracts.ICatalogModuleApi _catalog;
    private readonly CatalogContracts.IOfferModuleApi _offers;

    public CatalogGrpcService(
        CatalogContracts.ICatalogModuleApi catalog,
        CatalogContracts.IOfferModuleApi offers)
    {
        _catalog = catalog;
        _offers = offers;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES QUATRE RPC D'OFFRE — LE PANIER CLIENT EN DÉPEND.
    //
    // Le proto les déclarait depuis l'origine sur `CatalogApi` ; aucune n'était
    // implémentée. Un appel tombait donc en `UNIMPLEMENTED`, et le client levait
    // `NotSupportedException` avant même de partir.
    //
    // LES MONTANTS VOYAGENT EN CHAÎNE, ET C'EST LE PROTO QUI LE VEUT.
    //
    // Un `double` sur le fil arrondirait un prix ; un `int` de centimes n'a pas
    // de sens en franc CFA, qui n'a pas de subdivision en circulation. La chaîne
    // conserve le décimal exact. Elle est écrite et relue en culture INVARIANTE :
    // sur un serveur en locale française, « 12500.50 » deviendrait sinon
    // « 12500,50 » et le client le refuserait.
    //
    // CHAÎNE VIDE = ABSENT, JAMAIS ZÉRO. « Pas de promotion » et « gratuit »
    // sont deux faits différents, et proto3 ne distingue pas un champ absent d'un
    // champ à zéro sur un scalaire.
    // ═════════════════════════════════════════════════════════════════════════

    public override async Task<Proto.GetOfferResponse> GetOffer(
        Proto.GetOfferRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.OfferId, out var offerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "offer_id n'est pas un GUID."));
        }

        var offer = await _offers.GetOfferAsync(offerId, context.CancellationToken);
        return offer is null
            ? new Proto.GetOfferResponse { Found = false }
            : new Proto.GetOfferResponse { Found = true, Offer = ToProto(offer) };
    }

    public override async Task<Proto.GetOffersResponse> GetOffers(
        Proto.GetOffersRequest request, ServerCallContext context)
    {
        // UN IDENTIFIANT ILLISIBLE EST ÉCARTÉ, PAS FATAL. L'appelant demande un
        // LOT ; refuser les sept autres offres d'un panier parce que la huitième
        // ligne porte un identifiant abîmé viderait l'écran au lieu de le
        // dégrader.
        var ids = request.OfferIds
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        var offers = await _offers.GetOffersAsync(ids, context.CancellationToken);

        var response = new Proto.GetOffersResponse();
        response.Offers.AddRange(offers.Values.Select(ToProto));
        return response;
    }

    public override async Task<Proto.GetOffersResponse> ListPurchasableOffers(
        Proto.ListPurchasableOffersRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProductId, out var productId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "product_id n'est pas un GUID."));
        }

        var offers = await _offers.ListPurchasableOffersAsync(productId, context.CancellationToken);

        var response = new Proto.GetOffersResponse();
        response.Offers.AddRange(offers.Select(ToProto));
        return response;
    }

    public override async Task<Proto.GetOffersResponse> ListOffersBySku(
        Proto.ListOffersBySkuRequest request, ServerCallContext context)
    {
        var offers = await _offers.ListOffersBySkuAsync(request.Sku, context.CancellationToken);

        var response = new Proto.GetOffersResponse();
        response.Offers.AddRange(offers.Select(ToProto));
        return response;
    }

    private static Proto.OfferSummary ToProto(CatalogContracts.OfferSummary o)
    {
        var message = new Proto.OfferSummary
        {
            OfferId = o.Id.ToString(),
            ProductId = o.ProductId.ToString(),
            VariantId = o.VariantId.ToString(),
            StoreId = o.StoreId.ToString(),
            SellerId = o.SellerId.ToString(),
            Sku = o.Sku ?? string.Empty,
            BuyerPrice = Montant(o.BuyerPrice),
            EffectivePrice = Montant(o.EffectivePrice),
            Currency = o.Currency,
            Status = o.Status,
            IsPurchasable = o.IsPurchasable,
            Condition = o.Condition,
            HandlingTimeDays = o.HandlingTimeDays,
            ShipFromLocationId = o.ShipFromLocationId.ToString()
        };

        if (o.PromotionalPrice is { } promo)
        {
            message.PromotionalPrice = Montant(promo);
        }

        if (o.PromotionEndsOnUtc is { } fin)
        {
            // Aller-retour « O » : conserve la précision et le fuseau, et se
            // reparse sans ambiguïté. Un format court perdrait l'heure, et une
            // promotion finirait à minuit au lieu de 18 h.
            message.PromotionEndsOn = fin.ToString("O", CultureInfo.InvariantCulture);
        }

        return message;
    }

    private static string Montant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public override async Task<Proto.GetProductResponse> GetProduct(Proto.GetProductRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProductId, out var productId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "product_id n'est pas un GUID."));
        }

        var product = await _catalog.GetProductAsync(productId, context.CancellationToken);
        return product is null
            ? new Proto.GetProductResponse { Found = false }
            : new Proto.GetProductResponse { Found = true, Product = ToProto(product) };
    }

    public override async Task<Proto.GetCategoryResponse> GetCategory(Proto.GetCategoryRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CategoryId, out var categoryId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "category_id n'est pas un GUID."));
        }

        var category = await _catalog.GetCategoryAsync(categoryId, context.CancellationToken);
        return category is null
            ? new Proto.GetCategoryResponse { Found = false }
            : new Proto.GetCategoryResponse
            {
                Found = true,
                Category = new Proto.CategorySummary
                {
                    CategoryId = category.Id.ToString(),
                    Name = category.Name,
                    Status = category.Status
                }
            };
    }

    public override async Task<Proto.GetBrandResponse> GetBrand(Proto.GetBrandRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BrandId, out var brandId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "brand_id n'est pas un GUID."));
        }

        var brand = await _catalog.GetBrandAsync(brandId, context.CancellationToken);
        return brand is null
            ? new Proto.GetBrandResponse { Found = false }
            : new Proto.GetBrandResponse
            {
                Found = true,
                Brand = new Proto.BrandSummary
                {
                    BrandId = brand.Id.ToString(),
                    Name = brand.Name,
                    Status = brand.Status
                }
            };
    }

    private static Proto.ProductSummary ToProto(CatalogContracts.ProductSummary product)
    {
        var message = new Proto.ProductSummary
        {
            ProductId = product.Id.ToString(),
            SellerId = product.SellerId.ToString(),
            CategoryId = product.CategoryId.ToString(),
            Name = product.Name,
            Status = product.Status
        };

        if (product.BrandId is { } brandId)
        {
            message.BrandId = brandId.ToString();
        }

        var primaryMedia = product.Media.FirstOrDefault(media => media.IsPrimary);
        if (primaryMedia is not null)
        {
            message.PrimaryMediaUrl = primaryMedia.Url;
        }

        message.Variants.AddRange(product.Variants.Select(variant =>
        {
            var item = new Proto.ProductVariantSummary
            {
                VariantId = variant.Id.ToString(),
                Sku = variant.Sku,
                Status = "Active"
            };
            item.Attributes.Add(variant.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value));
            return item;
        }));

        return message;
    }
}

public sealed class ProductsGrpcClient : Contracts.IProductsModuleApi
{
    private readonly Proto.CatalogApi.CatalogApiClient _client;

    public ProductsGrpcClient(Proto.CatalogApi.CatalogApiClient client) => _client = client;

    public async Task<Contracts.ProductSummary?> GetProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetProductAsync(
            new Proto.GetProductRequest { ProductId = productId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Product) : null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES OFFRES EXISTENT, ET CE CODE EN EST LA PREUVE.
    //
    // Ces quatre méthodes levaient `NotSupportedException` : le module
    // Products/Offers n'était pas extrait, aucun service HBA ne détenait les
    // offres. Elles ont d'abord rendu `null` et des collections vides — ce qui
    // COMPILAIT, ne levait pas, et faisait conclure à l'appelant « cette offre
    // n'existe pas » alors qu'elle n'avait jamais été demandée à personne.
    //
    // C'est ce silence qui a laissé le défaut vivre : `AddItemToCartCommandHandler`
    // appelle `GetOfferAsync` pour lire le prix, et AUCUN article ne pouvait
    // entrer dans un panier — le premier geste du parcours client.
    //
    // La phase 3 a greffé les offres dans catalog-service. Les quatre RPC
    // existaient déjà dans `catalog.proto` ; il ne leur manquait qu'une
    // implémentation des deux côtés.
    //
    // UN MONTANT ILLISIBLE VAUT ZÉRO, ET C'EST DISCUTABLE.
    //
    // `Decimal` rend 0 plutôt que de lever si le serveur envoie une chaîne
    // inattendue. Le compromis est assumé pour une raison précise : ce chemin
    // sert le panier, et une exception de désérialisation y viderait l'écran
    // entier pour un champ. Un prix à zéro se voit ; il ne se vend pas
    // silencieusement, parce que `IsPurchasable` vient du serveur et non du prix.
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<Contracts.OfferSummary?> GetOfferAsync(
        Guid offerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetOfferAsync(
            new Proto.GetOfferRequest { OfferId = offerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Offer) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, Contracts.OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default)
    {
        if (offerIds.Count == 0)
        {
            return new Dictionary<Guid, Contracts.OfferSummary>();
        }

        var request = new Proto.GetOffersRequest();
        request.OfferIds.AddRange(offerIds.Select(id => id.ToString()));

        var response = await _client.GetOffersAsync(request, cancellationToken: cancellationToken);

        return response.Offers
            .Select(ToContract)
            .Where(offer => offer.Id != Guid.Empty)
            .ToDictionary(offer => offer.Id);
    }

    public async Task<IReadOnlyList<Contracts.OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListPurchasableOffersAsync(
            new Proto.ListPurchasableOffersRequest { ProductId = productId.ToString() },
            cancellationToken: cancellationToken);

        return response.Offers.Select(ToContract).ToList();
    }

    public async Task<IReadOnlyList<Contracts.OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListOffersBySkuAsync(
            new Proto.ListOffersBySkuRequest { Sku = sku },
            cancellationToken: cancellationToken);

        return response.Offers.Select(ToContract).ToList();
    }

    private static Contracts.OfferSummary ToContract(Proto.OfferSummary o)
        => new(
            Id: ParseGuid(o.OfferId),
            ProductId: ParseGuid(o.ProductId),
            VariantId: ParseGuid(o.VariantId),
            StoreId: ParseGuid(o.StoreId),
            SellerId: ParseGuid(o.SellerId),
            Sku: string.IsNullOrEmpty(o.Sku) ? null : o.Sku,
            BuyerPrice: Decimal(o.BuyerPrice),
            PromotionalPrice: string.IsNullOrEmpty(o.PromotionalPrice) ? null : Decimal(o.PromotionalPrice),
            EffectivePrice: Decimal(o.EffectivePrice),
            PromotionEndsOnUtc: Date(o.PromotionEndsOn),
            Currency: o.Currency,
            Status: o.Status,
            IsPurchasable: o.IsPurchasable,
            Condition: o.Condition,
            HandlingTimeDays: o.HandlingTimeDays,
            ShipFromLocationId: ParseGuid(o.ShipFromLocationId));

    /// <summary>
    /// Un montant venu du fil.
    /// </summary>
    /// <remarks>
    /// REFUSAIT DE RENDRE ZÉRO — voir <see cref="MontantSurLeFil"/>. Cette
    /// fonction s'écrivait « TryParse(…) ? valeur : 0m », comme six autres du
    /// dépôt : un champ non posé par l'émetteur — donc la chaîne VIDE, il n'y a
    /// pas de « non renseigné » pour un `string` protobuf 3 — se lisait « zéro
    /// franc ».
    ///
    /// `champ` EST REMPLI PAR LE COMPILATEUR, pas à la main. Il reçoit le TEXTE
    /// de l'expression passée — « order.AlreadyRefundedAmount » — donc un nom plus
    /// précis qu'aucun littéral recopié, et qui suit les renommages tout seul.
    /// </remarks>
    private static decimal Decimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);

    private static DateTime? Date(string? value)
        => string.IsNullOrEmpty(value)
            ? null
            : DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var date) ? date : null;

    private static Contracts.ProductSummary ToContract(Proto.ProductSummary product)
        => new(
            Id: ParseGuid(product.ProductId),
            SellerId: ParseGuid(product.SellerId),
            CategoryId: ParseGuid(product.CategoryId),
            BrandId: product.HasBrandId ? ParseGuid(product.BrandId) : null,
            Name: product.Name,
            Slug: Slugify(product.Name),
            Status: product.Status,
            // ═══════════════════════════════════════════════════════════════════
            // « Published », PAS « Active ». LE RENOMMAGE SE JOUE ICI.
            //
            // Le statut produit est comparé à une CHAÎNE LITTÉRALE, et cette ligne
            // est la seule du dépôt qui décide si un produit est visible pour les
            // appelants gRPC. Laissée sur « Active » après le passage aux huit
            // statuts du §5, elle aurait rendu INVISIBLE chaque produit en vente —
            // sans exception, sans journal, et sans rien qui relie la panne au
            // fichier ProductStatus.cs.
            //
            // « Active » est conservé en second terme le temps que toutes les bases
            // soient migrées : un appelant branché sur une base non reprise doit
            // continuer de voir ses produits. À retirer une fois la reprise faite
            // partout.
            // ═══════════════════════════════════════════════════════════════════
            IsVisible: string.Equals(product.Status, "Published", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(product.Status, "Active", StringComparison.OrdinalIgnoreCase),
            MainImageUrl: product.HasPrimaryMediaUrl ? product.PrimaryMediaUrl : null,
            Tags: []);

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    private static string Slugify(string value)
        => string.Join('-', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
}

public static class ProductsGrpcRegistration
{
    public static IServiceCollection AddProductsGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Services:Catalog est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.CatalogApi.CatalogApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<Contracts.IProductsModuleApi, ProductsGrpcClient>();

        return services;
    }
}
