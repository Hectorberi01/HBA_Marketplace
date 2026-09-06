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
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.FoodCarts.Contracts.Grpc` (lot B de la migration gRPC).
//
// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES
// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient
// l'implementation de seller-service ; les huit qui consomment order.proto
// liaient celle d'order-service. Aucun ne s'en servait.
//
// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.
// L'assemblage de contrats ne porte plus que le stub genere, le client et son
// enregistrement — le lot C descendra ces deux-la chez les appelants.
//
// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours
// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes
// intercepteurs. Un deplacement de fichier ne rend rien plus sur.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.FoodCarts.Api.Grpc.Services;

/// <summary>
/// Le service gRPC exposé par food-cart-service.
///
/// SANS `MapInternalGrpcService&lt;FoodCartGrpcService&gt;()` DANS `Program`,
/// food-order-service NE PEUT PAS LIRE LE PANIER.
///
/// Le client existe de l'autre côté, la configuration pointe la bonne adresse,
/// et l'appel rend `UNIMPLEMENTED`. Le symptôme apparaît au premier passage en
/// commande, pas au démarrage — la même erreur a déjà été faite côté commerce,
/// et le commentaire y est encore.
/// </summary>
internal sealed class FoodCartGrpcService : Proto.FoodCartApi.FoodCartApiBase
{
    private readonly Contracts.IFoodCartModuleApi _carts;

    public FoodCartGrpcService(Contracts.IFoodCartModuleApi carts) => _carts = carts;

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

    /// <summary>
    /// UN PANIER SANS IDENTIFIANT EST UN PANIER QUI N'EXISTE PAS.
    ///
    /// `GetActiveCartQuery` rend un panier VIDE plutôt qu'une erreur, pour que
    /// l'écran affiche « votre panier est vide ». Le rendre tel quel par le
    /// réseau ferait croire à food-order-service qu'il tient un panier — de
    /// `CartId` nul, sans restaurant, sans ligne — et la commande partirait à
    /// zéro franc au lieu d'être refusée.
    /// </summary>
    private static Proto.GetFoodCartResponse Repondre(Contracts.FoodCartSummary? cart)
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
