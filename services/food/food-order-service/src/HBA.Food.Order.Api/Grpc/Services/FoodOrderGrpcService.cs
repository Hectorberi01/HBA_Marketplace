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
// DEPLACE DEPUIS `HBA.FoodOrders.Contracts.Grpc` (lot B de la migration gRPC).

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
            CreatedOnUtc = commande.CreatedOnUtc.ToString("o", CultureInfo.InvariantCulture)
        };

        // L'ADRESSE DE REMISE — SANS ELLE, AUCUNE COURSE N'ÉTAIT CRÉÉE.
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

    /// <summary>Une coordonnée, en aller-retour EXACT.</summary>
    private static string Ecrire(double coordonnee)
        => coordonnee.ToString("R", CultureInfo.InvariantCulture);
}
