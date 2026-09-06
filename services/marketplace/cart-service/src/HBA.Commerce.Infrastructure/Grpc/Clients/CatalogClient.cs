using CatalogContracts = HBA.Catalog.Contracts;
using Contracts = HBA.Products.Contracts;
using Grpc.Core;
using HBA.Products.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Catalog.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Products.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 4 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Commerce.Infrastructure.Grpc.Clients;

internal sealed class ProductsGrpcClient : Contracts.IProductsModuleApi
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

internal static class ProductsGrpcRegistration
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
