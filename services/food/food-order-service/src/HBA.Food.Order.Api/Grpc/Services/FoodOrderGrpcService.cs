using Contracts = HBA.FoodOrders.Contracts;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.FoodOrders.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.FoodOrders.Contracts;
using ContratsFoodOrders = HBA.FoodOrders.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.FoodOrders.Contracts.Grpc` (lot B de la migration gRPC).
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

namespace HBA.FoodOrders.Api.Grpc.Services;

internal sealed class FoodOrderGrpcService : Proto.FoodOrderApi.FoodOrderApiBase
{
    private readonly ContratsFoodOrders.IMealOrderModuleApi _orders;

    public FoodOrderGrpcService(ContratsFoodOrders.IMealOrderModuleApi orders) => _orders = orders;

    public override async Task<Proto.GetMealOrderResponse> GetOrder(
        Proto.GetMealOrderRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "order_id n'est pas un GUID."));
        }

        var commande = await _orders.GetOrderAsync(orderId, context.CancellationToken);
        if (commande is null)
        {
            return new Proto.GetMealOrderResponse { Found = false };
        }

        var vue = new Proto.MealOrderView
        {
            OrderId = commande.OrderId.ToString(),
            BuyerId = commande.BuyerId.ToString(),
            RestaurantId = commande.RestaurantId.ToString(),
            Status = commande.Status,
            Subtotal = Ecrire(commande.Subtotal),
            ShippingFee = Ecrire(commande.ShippingFee),
            TotalAmount = Ecrire(commande.TotalAmount),
            Currency = commande.Currency,
            PromotionCode = commande.PromotionCode ?? string.Empty,
            DeliveryQuoteId = commande.DeliveryQuoteId ?? string.Empty,
            CustomerNote = commande.CustomerNote ?? string.Empty,

            // « o » — ALLER-RETOUR EXACT, ET FUSEAU CONSERVÉ.
            //
            // Un format court perdrait les millisecondes et le décalage, et la
            // date relue ne serait plus la même. Les commandes se trient et se
            // rapprochent par cet instant.
            CreatedOnUtc = commande.CreatedOnUtc.ToString("o", CultureInfo.InvariantCulture)
        };

        // L'ADRESSE DE REMISE — SANS ELLE, AUCUNE COURSE N'ÉTAIT CRÉÉE.
        //
        // restaurant-service la demandait à order-service, seul univers de
        // commandes qu'il connaissait ; une commande de repas y était introuvable
        // et le sac restait sur le passe. Voir `MealOrderShippingAddressSummary`.
        //
        // Le protobuf n'a pas de « nul » pour une chaîne : une adresse absente
        // rend huit chaînes vides, et c'est le client qui décide si cela vaut une
        // adresse ou non. Reconstruire un objet « présent mais vide » ici ferait
        // croire à une adresse là où il n'y en a pas.
        if (commande.ShippingAddress is { } adresse)
        {
            vue.ShipToRecipient = adresse.Recipient ?? string.Empty;
            vue.ShipToPhone = adresse.Phone ?? string.Empty;
            vue.ShipToCommuneName = adresse.CommuneName ?? string.Empty;
            vue.ShipToQuartier = adresse.Quartier ?? string.Empty;
            vue.ShipToLandmark = adresse.Landmark ?? string.Empty;
            vue.ShipToLine1 = adresse.Line1 ?? string.Empty;
            vue.ShipToLatitude = adresse.Latitude is { } lat ? Ecrire(lat) : string.Empty;
            vue.ShipToLongitude = adresse.Longitude is { } lon ? Ecrire(lon) : string.Empty;
        }

        foreach (var ligne in commande.Lines)
        {
            var l = new Proto.MealOrderLine
            {
                LineId = ligne.LineId.ToString(),
                MenuItemId = ligne.MenuItemId.ToString(),
                Name = ligne.Name,
                Quantity = ligne.Quantity,
                UnitPrice = Ecrire(ligne.UnitPrice),
                LineTotal = Ecrire(ligne.LineTotal),
                Currency = ligne.Currency,
                Notes = ligne.Notes ?? string.Empty
            };

            foreach (var option in ligne.Options)
            {
                l.Options.Add(new Proto.MealOrderLineOption
                {
                    OptionGroupId = option.OptionGroupId.ToString(),
                    OptionId = option.OptionId.ToString()
                });
            }

            vue.Lines.Add(l);
        }

        return new Proto.GetMealOrderResponse { Found = true, Order = vue };
    }

    public override async Task<Proto.HasPlacedMealOrderResponse> HasPlacedOrder(
        Proto.HasPlacedMealOrderRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "buyer_id n'est pas un GUID."));
        }

        return new Proto.HasPlacedMealOrderResponse
        {
            HasPlaced = await _orders.HasPlacedOrderAsync(buyerId, context.CancellationToken)
        };
    }

    private static string Ecrire(decimal montant) => montant.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Une coordonnée, en aller-retour EXACT.
    ///
    /// « R » ET NON LE FORMAT PAR DÉFAUT. Le défaut arrondit à quinze chiffres
    /// significatifs : la longitude relue n'est plus tout à fait celle écrite. Sur
    /// une adresse, l'écart se compte en centimètres et n'a aucune importance —
    /// mais un identifiant de point qui ne se compare plus à lui-même en a une,
    /// et cette valeur sert de clé de rapprochement chez delivery-service.
    /// </summary>
    private static string Ecrire(double coordonnee)
        => coordonnee.ToString("R", CultureInfo.InvariantCulture);
}
