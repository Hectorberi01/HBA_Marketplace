using Grpc.Core;
using HBA.Ordering.Contracts.Grpc;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Orders.Grpc.V1;
using ServiceLine = HBA.Orders.Contracts.OrderLineSummary;
using ServiceOrder = HBA.Orders.Contracts.OrderSummary;

using SharedLine = HBA.Ordering.Contracts.OrderLineSummary;
using SharedOrder = HBA.Ordering.Contracts.OrderSummary;
using System.Globalization;
using System.Runtime.CompilerServices;

using HBA.Ordering.Contracts;
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Ordering.Contracts.Grpc` (lot B de la migration gRPC).
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

namespace HBA.Orders.Api.Grpc.Services;

public sealed class OrderingGrpcService : Proto.OrderApi.OrderApiBase
{
    private readonly HBA.Orders.Contracts.IOrderingModuleApi _orders;

    public OrderingGrpcService(HBA.Orders.Contracts.IOrderingModuleApi orders) => _orders = orders;

    public override async Task<Proto.GetOrderResponse> GetOrder(Proto.GetOrderRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "order_id n'est pas un GUID."));
        }

        var order = await _orders.GetOrderAsync(orderId, context.CancellationToken);

        return order is null
            ? new Proto.GetOrderResponse { Found = false }
            : new Proto.GetOrderResponse { Found = true, Order = ToProto(order) };
    }

    public override async Task<Proto.ListOrdersResponse> ListOrdersByBuyer(
        Proto.ListOrdersByBuyerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "buyer_id n'est pas un GUID."));
        }

        var response = new Proto.ListOrdersResponse();
        if (await _orders.HasPlacedOrderAsync(buyerId, context.CancellationToken))
        {
            response.Orders.Add(new Proto.OrderSummary { BuyerId = buyerId.ToString(), Status = "Placed" });
        }

        return response;
    }

    /// <summary>
    /// Le compteur de ventes, compté par la base.
    /// </summary>
    /// <remarks>
    /// CE CORPS MANQUAIT, ET SON ABSENCE COÛTAIT LE COMPTEUR DE TOUS LES
    /// VENDEURS — voir l'encadré du RPC dans `order.proto`. Il délègue à
    /// `IOrderingModuleApi`, dont l'implémentation agrège en SQL : le filtre
    /// « commande payée » vit ainsi à un seul endroit, des deux côtés du réseau.
    /// </remarks>
    public override async Task<Proto.GetSellerSalesCountResponse> GetSellerSalesCount(
        Proto.GetSellerSalesCountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SellerId, out var sellerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "seller_id n'est pas un GUID."));
        }

        var ventes = await _orders.GetSellerSalesCountAsync(sellerId, context.CancellationToken);

        return new Proto.GetSellerSalesCountResponse { SalesCount = ventes };
    }

    public override async Task<Proto.GetOrderReturnContextResponse> GetOrderReturnContext(
        Proto.GetOrderReturnContextRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "order_id n'est pas un GUID."));
        }

        var returnContext = await _orders.GetOrderReturnContextAsync(orderId, context.CancellationToken);

        return returnContext is null
            ? new Proto.GetOrderReturnContextResponse { Found = false, Reason = "ORDER_NOT_RETURNABLE" }
            : new Proto.GetOrderReturnContextResponse
            {
                Found = true,
                Context = ToProto(returnContext)
            };
    }

    private static Proto.OrderReturnContext ToProto(HBA.Orders.Contracts.OrderReturnContext context)
    {
        var message = new Proto.OrderReturnContext
        {
            OrderId = context.OrderId.ToString(),
            CustomerId = context.CustomerId.ToString(),
            SellerId = context.SellerId.ToString(),
            StoreId = context.StoreId.ToString(),
            SellerOrderId = context.SellerOrderId?.ToString() ?? string.Empty,
            DeliveredAtUtc = context.DeliveredAtUtc.ToString("O", CultureInfo.InvariantCulture),
            PaymentId = context.PaymentId,
            Currency = context.Currency,
            CapturedAmount = Montant(context.CapturedAmount),
            AlreadyRefundedAmount = Montant(context.AlreadyRefundedAmount)
        };

        message.Lines.AddRange(context.Lines.Select(ToProto));
        return message;
    }

    private static Proto.OrderReturnLineContext ToProto(HBA.Orders.Contracts.OrderReturnLineContext line)
        => new()
        {
            OrderItemId = line.OrderItemId.ToString(),
            ProductId = line.ProductId.ToString(),
            VariantId = line.VariantId?.ToString() ?? string.Empty,
            CategoryId = line.CategoryId.ToString(),
            Sku = line.Sku,
            Name = line.Name,
            OrderedQuantity = line.OrderedQuantity,
            DeliveredQuantity = line.DeliveredQuantity,
            AlreadyReturnedQuantity = line.AlreadyReturnedQuantity,
            UnitPaidAmount = Montant(line.UnitPaidAmount)
        };

    private static Proto.OrderSummary ToProto(ServiceOrder order)
    {
        var message = new Proto.OrderSummary
        {
            OrderId = order.Id.ToString(),
            BuyerId = order.BuyerId.ToString(),
            Status = order.Status,
            Currency = order.Currency,
            TotalAmount = order.GrandTotal.ToString(CultureInfo.InvariantCulture),
            CartId = order.CartId.ToString(),
            CreatedAt = order.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            Subtotal = Montant(order.Subtotal),
            TotalSellerDiscount = Montant(order.TotalSellerDiscount),
            TotalPlatformDiscount = Montant(order.TotalPlatformDiscount),
            Kind = order.Kind,
            RestaurantId = order.RestaurantId?.ToString() ?? string.Empty,
            ShippingFee = Montant(order.ShippingFee),
            DeliveryQuoteId = order.DeliveryQuoteId ?? string.Empty
        };

        if (order.ShippingAddress is { } adresse)
        {
            var proto = new Proto.OrderShippingAddress
            {
                Label = adresse.Label ?? string.Empty,
                Recipient = adresse.Recipient ?? string.Empty,
                CommuneCode = adresse.CommuneCode ?? string.Empty,
                CommuneName = adresse.CommuneName ?? string.Empty,
                Quartier = adresse.Quartier ?? string.Empty,
                Landmark = adresse.Landmark ?? string.Empty,
                Line1 = adresse.Line1 ?? string.Empty,
                CountryCode = adresse.CountryCode ?? string.Empty,
                Phone = adresse.Phone ?? string.Empty
            };

            if (adresse.Latitude is { } lat) proto.Latitude = lat;
            if (adresse.Longitude is { } lon) proto.Longitude = lon;

            message.ShippingAddress = proto;
        }

        message.Lines.AddRange(order.Lines.Select(ToProto));
        return message;
    }

    private static Proto.OrderLineSummary ToProto(ServiceLine line)
    {
        var message = new Proto.OrderLineSummary
        {
            SellerId = line.SellerId.ToString(),
            ProductId = line.ProductId.ToString(),
            Sku = line.Sku,
            Quantity = line.Quantity,
            TotalAmount = Montant(line.LineTotal),
            Kind = line.Kind,
            OfferId = line.OfferId.ToString(),
            ShipFromLocationId = line.ShipFromLocationId.ToString(),
            UnitBasePrice = Montant(line.UnitBasePrice),
            SellerDiscount = Montant(line.SellerDiscount),
            PlatformDiscount = Montant(line.PlatformDiscount),
            FinalUnitPrice = Montant(line.FinalUnitPrice),
            RestaurantId = line.RestaurantId.ToString(),
            MenuItemId = line.MenuItemId.ToString(),
            Notes = line.Notes ?? string.Empty
        };

        if (line.Options is { Count: > 0 })
        {
            message.Options.AddRange(line.Options.Select(o => new Proto.OrderLineOption
            {
                OptionGroupId = o.OptionGroupId.ToString(),
                OptionId = o.OptionId.ToString()
            }));
        }

        return message;
    }

    private static string Montant(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
