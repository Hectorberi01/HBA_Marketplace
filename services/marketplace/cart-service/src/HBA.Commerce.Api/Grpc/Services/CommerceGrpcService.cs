using Contracts = HBA.Commerce.Contracts;

using Grpc.Core;
using HBA.Commerce.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.Commerce.Contracts;
using ContratsCommerce = HBA.Commerce.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// DEPLACE DEPUIS `HBA.Commerce.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Commerce.Api.Grpc.Services;

/// <summary>
/// Côté commerce-service : sert le panier valorisé à qui sait présenter le secret
/// interne.
/// </summary>
internal sealed class CommerceGrpcService : CommerceApi.CommerceApiBase
{
    private readonly ICartModuleApi _carts;

    public CommerceGrpcService(ICartModuleApi carts) => _carts = carts;

    public override async Task<GetCartResponse> GetActiveCart(
        GetActiveCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "buyer_id n'est pas un GUID."));
        }

        var cart = await _carts.GetActiveCartAsync(buyerId, context.CancellationToken);
        return Respond(cart);
    }

    public override async Task<GetCartResponse> GetCart(
        GetCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CartId, out var cartId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cart_id n'est pas un GUID."));
        }

        var cart = await _carts.GetCartAsync(cartId, context.CancellationToken);
        return Respond(cart);
    }

    // « PAS DE PANIER » N'EST PAS UNE ERREUR.
    private static GetCartResponse Respond(ContratsCommerce.CartSummary? cart)
        => cart is null
            ? new GetCartResponse { Found = false }
            : new GetCartResponse { Found = true, Cart = ToProto(cart) };

    private static CartView ToProto(ContratsCommerce.CartSummary cart)
    {
        var view = new CartView
        {
            CartId = cart.CartId.ToString(),
            BuyerId = cart.BuyerId.ToString(),
            Status = cart.Status,
            Currency = cart.Currency,
            Kind = cart.Kind ?? string.Empty,
            Subtotal = Money(cart.Subtotal),
            TotalSellerDiscount = Money(cart.TotalSellerDiscount),
            TotalPlatformDiscount = Money(cart.TotalPlatformDiscount),
            GrandTotal = Money(cart.GrandTotal),
            PromotionCode = cart.PromotionCode ?? string.Empty
        };

        foreach (var line in cart.Lines)
        {
            var message = new CartLine
            {
                LineId = line.LineId.ToString(),
                Kind = line.Kind,
                OfferId = line.OfferId.ToString(),
                ProductId = line.ProductId.ToString(),
                CategoryId = line.CategoryId.ToString(),
                SellerId = line.SellerId.ToString(),
                Sku = line.Sku,
                ShipFromLocationId = line.ShipFromLocationId.ToString(),
                Quantity = line.Quantity,
                UnitBaseAmount = Money(line.UnitBaseAmount),
                SellerDiscount = Money(line.SellerDiscount),
                PlatformDiscount = Money(line.PlatformDiscount),
                FinalUnitPrice = Money(line.FinalUnitPrice),
                LineTotal = Money(line.LineTotal),
                Currency = line.Currency,
                RestaurantId = line.RestaurantId.ToString(),
                MenuItemId = line.MenuItemId.ToString(),
                Notes = line.Notes ?? string.Empty
            };

            if (line.Options is { Count: > 0 })
            {
                foreach (var option in line.Options)
                {
                    message.Options.Add(new CartLineOption
                    {
                        OptionGroupId = option.OptionGroupId.ToString(),
                        OptionId = option.OptionId.ToString()
                    });
                }
            }

            view.Lines.Add(message);
        }

        return view;
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
