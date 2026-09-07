using Contracts = HBA.FoodOrders.Contracts;
using Grpc.Core;
using HBA.FoodOrders.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.FoodOrders.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using ContratsFoodOrders = HBA.FoodOrders.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.FoodOrders.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Communication.Notifications.Infrastructure.Grpc.Clients;

internal sealed class FoodOrderGrpcClient : ContratsFoodOrders.IMealOrderModuleApi
{
    private readonly Proto.FoodOrderApi.FoodOrderApiClient _client;

    public FoodOrderGrpcClient(Proto.FoodOrderApi.FoodOrderApiClient client) => _client = client;

    public async Task<ContratsFoodOrders.MealOrderSummary?> GetOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.GetOrderAsync(
            new Proto.GetMealOrderRequest { OrderId = orderId.ToString() },
            cancellationToken: cancellationToken);

        if (!reponse.Found || reponse.Order is null)
        {
            return null;
        }

        var o = reponse.Order;

        return new ContratsFoodOrders.MealOrderSummary(
            OrderId: ParseGuid(o.OrderId),
            BuyerId: ParseGuid(o.BuyerId),
            RestaurantId: ParseGuid(o.RestaurantId),
            Status: o.Status,
            Subtotal: ParseDecimal(o.Subtotal),
            ShippingFee: ParseDecimal(o.ShippingFee),
            TotalAmount: ParseDecimal(o.TotalAmount),
            Currency: o.Currency,
            PromotionCode: string.IsNullOrEmpty(o.PromotionCode) ? null : o.PromotionCode,
            DeliveryQuoteId: string.IsNullOrEmpty(o.DeliveryQuoteId) ? null : o.DeliveryQuoteId,
            CustomerNote: string.IsNullOrEmpty(o.CustomerNote) ? null : o.CustomerNote,
            CreatedOnUtc: ParseDate(o.CreatedOnUtc),
            Lines: o.Lines
                .Select(l => new ContratsFoodOrders.MealOrderLineSummary(
                    ParseGuid(l.LineId),
                    ParseGuid(l.MenuItemId),
                    l.Name,
                    l.Quantity,
                    ParseDecimal(l.UnitPrice),
                    ParseDecimal(l.LineTotal),
                    l.Currency,
                    string.IsNullOrEmpty(l.Notes) ? null : l.Notes,
                    l.Options
                        .Select(op => new ContratsFoodOrders.MealOrderLineOptionSummary(
                            ParseGuid(op.OptionGroupId), ParseGuid(op.OptionId)))
                        .ToList()))
                .ToList(),
            ShippingAddress: LireAdresse(o));
    }

    /// <summary>
    /// Reconstitue l'adresse de remise, ou <c> null</c> si le message n'en porte
    /// aucune.
    /// </summary>
    private static ContratsFoodOrders.MealOrderShippingAddressSummary? LireAdresse(Proto.MealOrderView o)
        => string.IsNullOrEmpty(o.ShipToLandmark)
            ? null
            : new ContratsFoodOrders.MealOrderShippingAddressSummary(
                Recipient: string.IsNullOrEmpty(o.ShipToRecipient) ? null : o.ShipToRecipient,
                Phone: string.IsNullOrEmpty(o.ShipToPhone) ? null : o.ShipToPhone,
                CommuneName: string.IsNullOrEmpty(o.ShipToCommuneName) ? null : o.ShipToCommuneName,
                Quartier: string.IsNullOrEmpty(o.ShipToQuartier) ? null : o.ShipToQuartier,
                Landmark: o.ShipToLandmark,
                Line1: string.IsNullOrEmpty(o.ShipToLine1) ? null : o.ShipToLine1,
                Latitude: ParseNullableDouble(o.ShipToLatitude),
                Longitude: ParseNullableDouble(o.ShipToLongitude));

    /// <summary>Une coordonnée, ou <c>null</c>.</summary>
    private static double? ParseNullableDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : null;

    public async Task<bool> HasPlacedOrderAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.HasPlacedOrderAsync(
            new Proto.HasPlacedMealOrderRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.HasPlaced;
    }

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    /// <summary>Un montant venu du fil.</summary>
    private static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);

    private static DateTime ParseDate(string? value)
        => DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var instant)
            ? instant
            : default;
}

internal static class FoodOrdersGrpcRegistration
{
    public static IServiceCollection AddFoodOrdersGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:FoodOrder"]
            ?? throw new InvalidOperationException("Services:FoodOrder est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.FoodOrderApi.FoodOrderApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<ContratsFoodOrders.IMealOrderModuleApi, FoodOrderGrpcClient>();

        return services;
    }
}
