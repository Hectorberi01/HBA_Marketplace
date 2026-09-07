using Contracts = HBA.FoodCarts.Contracts;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.FoodCarts.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.FoodCarts.Contracts;
using ContratsFoodCarts = HBA.FoodCarts.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// DEPLACE DEPUIS `HBA.FoodCarts.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.FoodCarts.Api.Grpc.Services;

/// <summary>Le service gRPC exposé par food-cart-service.</summary>
internal sealed class FoodCartGrpcService : Proto.FoodCartApi.FoodCartApiBase
{
    private readonly ContratsFoodCarts.IFoodCartModuleApi _carts;

    public FoodCartGrpcService(ContratsFoodCarts.IFoodCartModuleApi carts) => _carts = carts;

    public override async Task<Proto.GetFoodCartResponse> GetActiveCart(
        Proto.GetActiveFoodCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "buyer_id n'est pas un GUID."));
        }

        var cart = await _carts.GetActiveCartAsync(buyerId, context.CancellationToken);
        return Repondre(cart);
    }

    public override async Task<Proto.GetFoodCartResponse> GetCart(
        Proto.GetFoodCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CartId, out var cartId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cart_id n'est pas un GUID."));
        }

        var cart = await _carts.GetCartAsync(cartId, context.CancellationToken);
        return Repondre(cart);
    }

    /// <summary>UN PANIER SANS IDENTIFIANT EST UN PANIER QUI N'EXISTE PAS.</summary>
    private static Proto.GetFoodCartResponse Repondre(ContratsFoodCarts.FoodCartSummary? cart)
    {
        if (cart is null || cart.CartId == Guid.Empty)
        {
            return new Proto.GetFoodCartResponse { Found = false };
        }

        var vue = new Proto.FoodCartView
        {
            CartId = cart.CartId.ToString(),
            BuyerId = cart.BuyerId.ToString(),
            RestaurantId = (cart.RestaurantId ?? Guid.Empty).ToString(),
            Status = cart.Status,
            Currency = cart.Currency,
            Subtotal = Ecrire(cart.Subtotal),
            TotalSellerDiscount = Ecrire(cart.TotalSellerDiscount),
            TotalPlatformDiscount = Ecrire(cart.TotalPlatformDiscount),
            GrandTotal = Ecrire(cart.GrandTotal),
            PromotionCode = cart.PromotionCode ?? string.Empty
        };

        foreach (var ligne in cart.Lines)
        {
            var l = new Proto.FoodCartLine
            {
                LineId = ligne.LineId.ToString(),
                MenuItemId = ligne.MenuItemId.ToString(),
                Name = ligne.Name,
                Quantity = ligne.Quantity,
                UnitBaseAmount = Ecrire(ligne.UnitBaseAmount),
                SellerDiscount = Ecrire(ligne.SellerDiscount),
                PlatformDiscount = Ecrire(ligne.PlatformDiscount),
                FinalUnitPrice = Ecrire(ligne.FinalUnitPrice),
                LineTotal = Ecrire(ligne.LineTotal),
                Currency = ligne.Currency,
                Notes = ligne.Notes ?? string.Empty
            };

            foreach (var option in ligne.Options)
            {
                l.Options.Add(new Proto.FoodCartLineOption
                {
                    OptionGroupId = option.OptionGroupId.ToString(),
                    OptionId = option.OptionId.ToString()
                });
            }

            vue.Lines.Add(l);
        }

        return new Proto.GetFoodCartResponse { Found = true, Cart = vue };
    }

    private static string Ecrire(decimal montant)
        => montant.ToString(CultureInfo.InvariantCulture);
}
