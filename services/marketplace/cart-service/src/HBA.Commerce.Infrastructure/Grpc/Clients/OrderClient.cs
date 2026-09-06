using Grpc.Core;
using HBA.Commerce.Infrastructure.Grpc.Mappers;
using HBA.Ordering.Contracts;
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

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Ordering.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 8 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Commerce.Infrastructure.Grpc.Clients;

internal sealed class OrderingGrpcClient : IOrderingModuleApi
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

internal sealed class OrdersGrpcClient : HBA.Orders.Contracts.IOrderingModuleApi
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

internal static class OrderingGrpcRegistration
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
