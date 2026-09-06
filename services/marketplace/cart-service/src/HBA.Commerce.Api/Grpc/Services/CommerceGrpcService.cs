using Contracts = HBA.Commerce.Contracts;

using Grpc.Core;
using HBA.Commerce.Contracts.Grpc;
using HBA.Commerce.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.Commerce.Contracts;
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Commerce.Contracts.Grpc` (lot B de la migration gRPC).
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

namespace HBA.Commerce.Api.Grpc.Services;

/// <summary>
/// Côté commerce-service : sert le panier valorisé à qui sait présenter le
/// secret interne.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE CONTRAT EXISTE.
///
/// `PlaceOrderCommandHandler` lit le panier pour figer ses prix dans une
/// commande. Dans le monolithe, il appelait `ICartModuleApi` en mémoire. Une
/// fois Ordering et Cart séparés en deux services, l'interface était toujours
/// injectée et plus personne ne la fournissait : le conteneur d'order-service
/// refusait de démarrer.
///
/// C'EST UNE LECTURE SUR LE CHEMIN CRITIQUE.
///
/// Sans réponse de commerce-service, aucune commande ne peut être passée.
/// L'échéance de cinq secondes posée par `InternalCallClientInterceptor` vaut
/// donc ici comme ailleurs : un panier qui ne répond pas doit rendre un refus
/// franc plutôt que retenir la requête de l'acheteur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CommerceGrpcService : CommerceApi.CommerceApiBase
{
    private readonly Contracts.ICartModuleApi _carts;

    public CommerceGrpcService(Contracts.ICartModuleApi carts) => _carts = carts;

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
    //
    // `NotFound` obligerait l'appelant à rattraper une RpcException pour un cas
    // parfaitement normal — un acheteur qui n'a rien mis dans son panier. Le
    // drapeau `found` distingue « pas de panier » de « le service n'a pas
    // répondu », et seule la seconde situation mérite une exception.
    private static GetCartResponse Respond(Contracts.CartSummary? cart)
        => cart is null
            ? new GetCartResponse { Found = false }
            : new GetCartResponse { Found = true, Cart = ToProto(cart) };

    private static CartView ToProto(Contracts.CartSummary cart)
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
