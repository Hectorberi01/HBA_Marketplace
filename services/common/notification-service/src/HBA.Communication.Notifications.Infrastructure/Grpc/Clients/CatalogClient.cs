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

using ContratsProducts = HBA.Products.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Products.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Communication.Notifications.Infrastructure.Grpc.Clients;

internal sealed class ProductsGrpcClient : ContratsProducts.IProductsModuleApi
{
    private readonly Proto.CatalogApi.CatalogApiClient _client;

    public ProductsGrpcClient(Proto.CatalogApi.CatalogApiClient client) => _client = client;

    public async Task<ContratsProducts.ProductSummary?> GetProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetProductAsync(
            new Proto.GetProductRequest { ProductId = productId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Product) : null;
    }

    // LES OFFRES EXISTENT, ET CE CODE EN EST LA PREUVE.

    public async Task<ContratsProducts.OfferSummary?> GetOfferAsync(
        Guid offerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetOfferAsync(
            new Proto.GetOfferRequest { OfferId = offerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Offer) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, ContratsProducts.OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default)
    {
        if (offerIds.Count == 0)
        {
            return new Dictionary<Guid, ContratsProducts.OfferSummary>();
        }

        var request = new Proto.GetOffersRequest();
        request.OfferIds.AddRange(offerIds.Select(id => id.ToString()));

        var response = await _client.GetOffersAsync(request, cancellationToken: cancellationToken);

        return response.Offers
            .Select(ToContract)
            .Where(offer => offer.Id != Guid.Empty)
            .ToDictionary(offer => offer.Id);
    }

    public async Task<IReadOnlyList<ContratsProducts.OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListPurchasableOffersAsync(
            new Proto.ListPurchasableOffersRequest { ProductId = productId.ToString() },
            cancellationToken: cancellationToken);

        return response.Offers.Select(ToContract).ToList();
    }

    public async Task<IReadOnlyList<ContratsProducts.OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListOffersBySkuAsync(
            new Proto.ListOffersBySkuRequest { Sku = sku },
            cancellationToken: cancellationToken);

        return response.Offers.Select(ToContract).ToList();
    }

    private static ContratsProducts.OfferSummary ToContract(Proto.OfferSummary o)
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

    /// <summary>Un montant venu du fil.</summary>
    private static decimal Decimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);

    private static DateTime? Date(string? value)
        => string.IsNullOrEmpty(value)
            ? null
            : DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var date) ? date : null;

    private static ContratsProducts.ProductSummary ToContract(Proto.ProductSummary product)
        => new(
            Id: ParseGuid(product.ProductId),
            SellerId: ParseGuid(product.SellerId),
            CategoryId: ParseGuid(product.CategoryId),
            BrandId: product.HasBrandId ? ParseGuid(product.BrandId) : null,
            Name: product.Name,
            Slug: Slugify(product.Name),
            Status: product.Status,
            // « Published », PAS « Active ».
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

        services.AddScoped<ContratsProducts.IProductsModuleApi, ProductsGrpcClient>();

        return services;
    }
}
