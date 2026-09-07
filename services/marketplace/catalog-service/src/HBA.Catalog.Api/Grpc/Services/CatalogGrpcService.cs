using CatalogContracts = HBA.Catalog.Contracts;
using Contracts = HBA.Products.Contracts;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Catalog.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.Products.Contracts;
// DEPLACE DEPUIS `HBA.Products.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Catalog.Api.Grpc.Services;

internal sealed class CatalogGrpcService : Proto.CatalogApi.CatalogApiBase
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

    // LES QUATRE RPC D'OFFRE — LE PANIER CLIENT EN DÉPEND.

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
        // UN IDENTIFIANT ILLISIBLE EST ÉCARTÉ, PAS FATAL. L'appelant demande un LOT
        // ; refuser les sept autres offres d'un panier parce que la huitième ligne
        // porte un identifiant abîmé viderait l'écran au lieu de le dégrader.
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
            // reparse sans ambiguïté.
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
