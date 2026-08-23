using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Orders.Grpc.V1;
using SharedLine = HBA.Ordering.Contracts.OrderLineSummary;
using SharedOrder = HBA.Ordering.Contracts.OrderSummary;
using ServiceLine = HBA.Orders.Contracts.OrderLineSummary;
using ServiceOrder = HBA.Orders.Contracts.OrderSummary;

namespace HBA.Ordering.Contracts.Grpc;

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

public sealed class OrderingGrpcClient : IOrderingModuleApi
{
    private readonly Proto.OrderApi.OrderApiClient _client;

    public OrderingGrpcClient(Proto.OrderApi.OrderApiClient client) => _client = client;

    public async Task<SharedOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetOrderAsync(
            new Proto.GetOrderRequest { OrderId = orderId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Order) : null;
    }

    public async Task<HBA.Ordering.Contracts.OrderReturnContext?> GetOrderReturnContextAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetOrderReturnContextAsync(
            new Proto.GetOrderReturnContextRequest { OrderId = orderId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToSharedReturnContext(response.Context) : null;
    }

    public async Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListOrdersByBuyerAsync(
            new Proto.ListOrdersByBuyerRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Orders.Count > 0;
    }

    /// <summary>
    /// Le compteur de ventes du vendeur.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE PASSAIT PAR `ListOrdersBySeller`, QUI N'A JAMAIS EU DE
    /// CORPS DE SERVEUR.
    ///
    /// Elle est appelée par `SellerSalesCountHandler` à CHAQUE commande confirmée.
    /// Elle rendait donc `UNIMPLEMENTED` à chaque fois — avant que l'inbox ne soit
    /// marquée, donc avec rejeu du message — et `SalesCount` restait à zéro pour
    /// tous les vendeurs. Le handler avait précisément été écrit pour le remplir.
    ///
    /// ET ELLE REFAISAIT LE TRI DES STATUTS ELLE-MÊME.
    ///
    /// Elle filtrait `Confirmed`/`Delivered` sur les lignes reçues, alors que la
    /// version in-process le fait en SQL : même interface, deux réponses possibles
    /// selon le côté du réseau où vivait le lecteur. Le serveur rend maintenant le
    /// NOMBRE, et il n'y a plus qu'un endroit où « une vente est une vente payée »
    /// est écrit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerSalesCountAsync(
            new Proto.GetSellerSalesCountRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.SalesCount;
    }


    // ═════════════════════════════════════════════════════════════════════
    // ON NE COMBLE PLUS LES TROUS EN INVENTANT.
    //
    // Cette conversion forçait `Kind` à « Goods », mettait `CartId` à vide,
    // `CreatedAtUtc` à `DateTime.MinValue`, les remises à zéro, et recalculait
    // un prix unitaire en divisant le total par la quantité.
    //
    // Toute commande de REPAS revenait donc en commande de MARCHANDISE : plat,
    // options et note disparaissaient sans un mot, et food-service ne pouvait
    // pas ouvrir de ticket de cuisine à partir de ce qu'il recevait.
    //
    // Le message proto porte désormais les dix-sept champs du contrat ; la
    // conversion se contente de traduire.
    // ═════════════════════════════════════════════════════════════════════
    private static SharedOrder ToContract(Proto.OrderSummary order)
        => new(
            Id: OrderingGrpcParsing.ParseGuid(order.OrderId),
            BuyerId: OrderingGrpcParsing.ParseGuid(order.BuyerId),
            CartId: OrderingGrpcParsing.ParseGuid(order.CartId),
            Currency: string.IsNullOrWhiteSpace(order.Currency) ? "XOF" : order.Currency,
            Status: order.Status,
            CreatedAtUtc: OrderingGrpcParsing.ParseDate(order.CreatedAt),
            Subtotal: OrderingGrpcParsing.ParseDecimal(order.Subtotal),
            TotalSellerDiscount: OrderingGrpcParsing.ParseDecimal(order.TotalSellerDiscount),
            TotalPlatformDiscount: OrderingGrpcParsing.ParseDecimal(order.TotalPlatformDiscount),
            GrandTotal: OrderingGrpcParsing.ParseDecimal(order.TotalAmount),
            Lines: order.Lines.Select(ToContractLigne).ToList(),
            ShippingAddress: ToContractAdresse(order.ShippingAddress),
            ShippingFee: OrderingGrpcParsing.ParseDecimal(order.ShippingFee),
            Kind: string.IsNullOrEmpty(order.Kind) ? "Goods" : order.Kind,
            RestaurantId: string.IsNullOrEmpty(order.RestaurantId)
                ? null
                : OrderingGrpcParsing.ParseGuid(order.RestaurantId),
            DeliveryQuoteId: OrderingGrpcParsing.Vide(order.DeliveryQuoteId));

    private static HBA.Ordering.Contracts.OrderShippingAddressSummary? ToContractAdresse(Proto.OrderShippingAddress? a)
        => a is null
            ? null
            : new(
                Label: OrderingGrpcParsing.Vide(a.Label),
                Recipient: OrderingGrpcParsing.Vide(a.Recipient),
                CommuneCode: OrderingGrpcParsing.Vide(a.CommuneCode),
                CommuneName: OrderingGrpcParsing.Vide(a.CommuneName),
                Quartier: OrderingGrpcParsing.Vide(a.Quartier),
                Landmark: OrderingGrpcParsing.Vide(a.Landmark),
                Line1: OrderingGrpcParsing.Vide(a.Line1),
                CountryCode: OrderingGrpcParsing.Vide(a.CountryCode),
                Latitude: a.HasLatitude ? a.Latitude : null,
                Longitude: a.HasLongitude ? a.Longitude : null,
                Phone: OrderingGrpcParsing.Vide(a.Phone));

    private static SharedLine ToContractLigne(Proto.OrderLineSummary line)
        => new(
            Kind: string.IsNullOrEmpty(line.Kind) ? "Goods" : line.Kind,
            OfferId: OrderingGrpcParsing.ParseGuid(line.OfferId),
            ProductId: OrderingGrpcParsing.ParseGuid(line.ProductId),
            SellerId: OrderingGrpcParsing.ParseGuid(line.SellerId),
            Sku: line.Sku,
            ShipFromLocationId: OrderingGrpcParsing.ParseGuid(line.ShipFromLocationId),
            Quantity: line.Quantity,
            UnitBasePrice: OrderingGrpcParsing.ParseDecimal(line.UnitBasePrice),
            SellerDiscount: OrderingGrpcParsing.ParseDecimal(line.SellerDiscount),
            PlatformDiscount: OrderingGrpcParsing.ParseDecimal(line.PlatformDiscount),
            FinalUnitPrice: OrderingGrpcParsing.ParseDecimal(line.FinalUnitPrice),
            LineTotal: OrderingGrpcParsing.ParseDecimal(line.TotalAmount),
            RestaurantId: OrderingGrpcParsing.ParseGuid(line.RestaurantId),
            MenuItemId: OrderingGrpcParsing.ParseGuid(line.MenuItemId),
            Notes: OrderingGrpcParsing.Vide(line.Notes),
            Options: line.Options.Count == 0
                ? null
                : line.Options
                    .Select(o => new HBA.Ordering.Contracts.OrderLineOptionSummary(
                        OrderingGrpcParsing.ParseGuid(o.OptionGroupId),
                        OrderingGrpcParsing.ParseGuid(o.OptionId)))
                    .ToList());

    private static HBA.Ordering.Contracts.OrderReturnContext ToSharedReturnContext(Proto.OrderReturnContext context)
        => new(
            OrderId: OrderingGrpcParsing.ParseGuid(context.OrderId),
            CustomerId: OrderingGrpcParsing.ParseGuid(context.CustomerId),
            SellerId: OrderingGrpcParsing.ParseGuid(context.SellerId),
            StoreId: OrderingGrpcParsing.ParseGuid(context.StoreId),
            SellerOrderId: string.IsNullOrEmpty(context.SellerOrderId)
                ? null
                : OrderingGrpcParsing.ParseGuid(context.SellerOrderId),
            DeliveredAtUtc: OrderingGrpcParsing.ParseDate(context.DeliveredAtUtc),
            PaymentId: context.PaymentId,
            Currency: string.IsNullOrWhiteSpace(context.Currency) ? "XOF" : context.Currency,
            CapturedAmount: OrderingGrpcParsing.ParseDecimal(context.CapturedAmount),
            AlreadyRefundedAmount: OrderingGrpcParsing.ParseDecimal(context.AlreadyRefundedAmount),
            Lines: context.Lines.Select(ToSharedReturnLine).ToList());

    private static HBA.Ordering.Contracts.OrderReturnLineContext ToSharedReturnLine(Proto.OrderReturnLineContext line)
        => new(
            OrderItemId: OrderingGrpcParsing.ParseGuid(line.OrderItemId),
            ProductId: OrderingGrpcParsing.ParseGuid(line.ProductId),
            VariantId: string.IsNullOrEmpty(line.VariantId)
                ? null
                : OrderingGrpcParsing.ParseGuid(line.VariantId),
            CategoryId: OrderingGrpcParsing.ParseGuid(line.CategoryId),
            Sku: line.Sku,
            Name: line.Name,
            OrderedQuantity: line.OrderedQuantity,
            DeliveredQuantity: line.DeliveredQuantity,
            AlreadyReturnedQuantity: line.AlreadyReturnedQuantity,
            UnitPaidAmount: OrderingGrpcParsing.ParseDecimal(line.UnitPaidAmount));
}

public sealed class OrdersGrpcClient : HBA.Orders.Contracts.IOrderingModuleApi
{
    private readonly Proto.OrderApi.OrderApiClient _client;

    public OrdersGrpcClient(Proto.OrderApi.OrderApiClient client) => _client = client;

    public async Task<ServiceOrder?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetOrderAsync(
            new Proto.GetOrderRequest { OrderId = orderId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToServiceContract(response.Order) : null;
    }

    public async Task<HBA.Orders.Contracts.OrderReturnContext?> GetOrderReturnContextAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.GetOrderReturnContextAsync(
            new Proto.GetOrderReturnContextRequest { OrderId = orderId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToServiceReturnContext(response.Context) : null;
    }

    public async Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.ListOrdersByBuyerAsync(
            new Proto.ListOrdersByBuyerRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return response.Orders.Count > 0;
    }

    /// <summary>
    /// Le compteur de ventes du vendeur.
    /// </summary>
    /// <remarks>
    /// MÊME CORRECTION QUE DANS `OrderingGrpcClient`, ET IL FALLAIT LES DEUX.
    ///
    /// Ce second client sert l'autre interface `IOrderingModuleApi` du dépôt —
    /// celle de `HBA.Orders.Contracts`. Il passait lui aussi par
    /// `ListOrdersBySeller`, un RPC sans corps de serveur, et refaisait le filtre
    /// de statut de son côté. Personne ne l'appelle aujourd'hui ; le laisser
    /// aurait posé un `UNIMPLEMENTED` en embuscade pour le premier qui l'aurait
    /// fait.
    ///
    /// DEUX INTERFACES DE MÊME NOM DANS DEUX NAMESPACES, c'est un reste de
    /// nommage à traiter en 9.5 — pas ici.
    /// </remarks>
    public async Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetSellerSalesCountAsync(
            new Proto.GetSellerSalesCountRequest { SellerId = sellerId.ToString() },
            cancellationToken: cancellationToken);

        return response.SalesCount;
    }

    // ═════════════════════════════════════════════════════════════════════
    // ON NE COMBLE PLUS LES TROUS EN INVENTANT.
    //
    // Cette conversion forçait `Kind` à « Goods », mettait `CartId` à vide,
    // `CreatedAtUtc` à `DateTime.MinValue`, les remises à zéro, et recalculait
    // un prix unitaire en divisant le total par la quantité.
    //
    // Toute commande de REPAS revenait donc en commande de MARCHANDISE : plat,
    // options et note disparaissaient sans un mot, et food-service ne pouvait
    // pas ouvrir de ticket de cuisine à partir de ce qu'il recevait.
    //
    // Le message proto porte désormais les dix-sept champs du contrat ; la
    // conversion se contente de traduire.
    // ═════════════════════════════════════════════════════════════════════
    private static ServiceOrder ToServiceContract(Proto.OrderSummary order)
        => new(
            Id: OrderingGrpcParsing.ParseGuid(order.OrderId),
            BuyerId: OrderingGrpcParsing.ParseGuid(order.BuyerId),
            CartId: OrderingGrpcParsing.ParseGuid(order.CartId),
            Currency: string.IsNullOrWhiteSpace(order.Currency) ? "XOF" : order.Currency,
            Status: order.Status,
            CreatedAtUtc: OrderingGrpcParsing.ParseDate(order.CreatedAt),
            Subtotal: OrderingGrpcParsing.ParseDecimal(order.Subtotal),
            TotalSellerDiscount: OrderingGrpcParsing.ParseDecimal(order.TotalSellerDiscount),
            TotalPlatformDiscount: OrderingGrpcParsing.ParseDecimal(order.TotalPlatformDiscount),
            GrandTotal: OrderingGrpcParsing.ParseDecimal(order.TotalAmount),
            Lines: order.Lines.Select(ToServiceContractLigne).ToList(),
            ShippingAddress: ToServiceContractAdresse(order.ShippingAddress),
            ShippingFee: OrderingGrpcParsing.ParseDecimal(order.ShippingFee),
            Kind: string.IsNullOrEmpty(order.Kind) ? "Goods" : order.Kind,
            RestaurantId: string.IsNullOrEmpty(order.RestaurantId)
                ? null
                : OrderingGrpcParsing.ParseGuid(order.RestaurantId),
            DeliveryQuoteId: OrderingGrpcParsing.Vide(order.DeliveryQuoteId));

    private static HBA.Orders.Contracts.OrderShippingAddressSummary? ToServiceContractAdresse(Proto.OrderShippingAddress? a)
        => a is null
            ? null
            : new(
                Label: OrderingGrpcParsing.Vide(a.Label),
                Recipient: OrderingGrpcParsing.Vide(a.Recipient),
                CommuneCode: OrderingGrpcParsing.Vide(a.CommuneCode),
                CommuneName: OrderingGrpcParsing.Vide(a.CommuneName),
                Quartier: OrderingGrpcParsing.Vide(a.Quartier),
                Landmark: OrderingGrpcParsing.Vide(a.Landmark),
                Line1: OrderingGrpcParsing.Vide(a.Line1),
                CountryCode: OrderingGrpcParsing.Vide(a.CountryCode),
                Latitude: a.HasLatitude ? a.Latitude : null,
                Longitude: a.HasLongitude ? a.Longitude : null,
                Phone: OrderingGrpcParsing.Vide(a.Phone));

    private static ServiceLine ToServiceContractLigne(Proto.OrderLineSummary line)
        => new(
            Kind: string.IsNullOrEmpty(line.Kind) ? "Goods" : line.Kind,
            OfferId: OrderingGrpcParsing.ParseGuid(line.OfferId),
            ProductId: OrderingGrpcParsing.ParseGuid(line.ProductId),
            SellerId: OrderingGrpcParsing.ParseGuid(line.SellerId),
            Sku: line.Sku,
            ShipFromLocationId: OrderingGrpcParsing.ParseGuid(line.ShipFromLocationId),
            Quantity: line.Quantity,
            UnitBasePrice: OrderingGrpcParsing.ParseDecimal(line.UnitBasePrice),
            SellerDiscount: OrderingGrpcParsing.ParseDecimal(line.SellerDiscount),
            PlatformDiscount: OrderingGrpcParsing.ParseDecimal(line.PlatformDiscount),
            FinalUnitPrice: OrderingGrpcParsing.ParseDecimal(line.FinalUnitPrice),
            LineTotal: OrderingGrpcParsing.ParseDecimal(line.TotalAmount),
            RestaurantId: OrderingGrpcParsing.ParseGuid(line.RestaurantId),
            MenuItemId: OrderingGrpcParsing.ParseGuid(line.MenuItemId),
            Notes: OrderingGrpcParsing.Vide(line.Notes),
            Options: line.Options.Count == 0
                ? null
                : line.Options
                    .Select(o => new HBA.Orders.Contracts.OrderLineOptionSummary(
                        OrderingGrpcParsing.ParseGuid(o.OptionGroupId),
                        OrderingGrpcParsing.ParseGuid(o.OptionId)))
                    .ToList());

    private static HBA.Orders.Contracts.OrderReturnContext ToServiceReturnContext(Proto.OrderReturnContext context)
        => new(
            OrderId: OrderingGrpcParsing.ParseGuid(context.OrderId),
            CustomerId: OrderingGrpcParsing.ParseGuid(context.CustomerId),
            SellerId: OrderingGrpcParsing.ParseGuid(context.SellerId),
            StoreId: OrderingGrpcParsing.ParseGuid(context.StoreId),
            SellerOrderId: string.IsNullOrEmpty(context.SellerOrderId)
                ? null
                : OrderingGrpcParsing.ParseGuid(context.SellerOrderId),
            DeliveredAtUtc: OrderingGrpcParsing.ParseDate(context.DeliveredAtUtc),
            PaymentId: context.PaymentId,
            Currency: string.IsNullOrWhiteSpace(context.Currency) ? "XOF" : context.Currency,
            CapturedAmount: OrderingGrpcParsing.ParseDecimal(context.CapturedAmount),
            AlreadyRefundedAmount: OrderingGrpcParsing.ParseDecimal(context.AlreadyRefundedAmount),
            Lines: context.Lines.Select(ToServiceReturnLine).ToList());

    private static HBA.Orders.Contracts.OrderReturnLineContext ToServiceReturnLine(Proto.OrderReturnLineContext line)
        => new(
            OrderItemId: OrderingGrpcParsing.ParseGuid(line.OrderItemId),
            ProductId: OrderingGrpcParsing.ParseGuid(line.ProductId),
            VariantId: string.IsNullOrEmpty(line.VariantId)
                ? null
                : OrderingGrpcParsing.ParseGuid(line.VariantId),
            CategoryId: OrderingGrpcParsing.ParseGuid(line.CategoryId),
            Sku: line.Sku,
            Name: line.Name,
            OrderedQuantity: line.OrderedQuantity,
            DeliveredQuantity: line.DeliveredQuantity,
            AlreadyReturnedQuantity: line.AlreadyReturnedQuantity,
            UnitPaidAmount: OrderingGrpcParsing.ParseDecimal(line.UnitPaidAmount));
}

internal static class OrderingGrpcParsing
{
    public static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    public static DateTime ParseDate(string? value)
        => DateTime.TryParse(
               value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTime.MinValue;

    // Chaîne vide et null se confondent en protobuf3 : un champ absent arrive
    // comme "". Rendre "" là où le contrat attend `null` ferait afficher des
    // valeurs vides au lieu de « non renseigné ».
    public static string? Vide(string? value) => string.IsNullOrEmpty(value) ? null : value;

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
    public static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}

public static class OrderingGrpcRegistration
{
    public static IServiceCollection AddOrderingGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Order"]
            ?? throw new InvalidOperationException("Services:Order est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.OrderApi.OrderApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<IOrderingModuleApi, OrderingGrpcClient>();
        services.AddScoped<HBA.Orders.Contracts.IOrderingModuleApi, OrdersGrpcClient>();

        return services;
    }
}
