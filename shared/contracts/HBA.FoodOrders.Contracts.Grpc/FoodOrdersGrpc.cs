using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Contracts = HBA.FoodOrders.Contracts;
using Proto = HBA.FoodOrders.Grpc.V1;

namespace HBA.FoodOrders.Contracts.Grpc;



public sealed class FoodOrderGrpcClient : Contracts.IMealOrderModuleApi
{
    private readonly Proto.FoodOrderApi.FoodOrderApiClient _client;

    public FoodOrderGrpcClient(Proto.FoodOrderApi.FoodOrderApiClient client) => _client = client;

    public async Task<Contracts.MealOrderSummary?> GetOrderAsync(
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

        return new Contracts.MealOrderSummary(
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
                .Select(l => new Contracts.MealOrderLineSummary(
                    ParseGuid(l.LineId),
                    ParseGuid(l.MenuItemId),
                    l.Name,
                    l.Quantity,
                    ParseDecimal(l.UnitPrice),
                    ParseDecimal(l.LineTotal),
                    l.Currency,
                    string.IsNullOrEmpty(l.Notes) ? null : l.Notes,
                    l.Options
                        .Select(op => new Contracts.MealOrderLineOptionSummary(
                            ParseGuid(op.OptionGroupId), ParseGuid(op.OptionId)))
                        .ToList()))
                .ToList(),
            ShippingAddress: LireAdresse(o));
    }

    /// <summary>
    /// Reconstitue l'adresse de remise, ou <c>null</c> si le message n'en porte
    /// aucune.
    ///
    /// LE CRITÈRE EST LE REPÈRE, PAS LA CHAÎNE VIDE DE CHAQUE CHAMP.
    ///
    /// Le protobuf ne distingue pas « champ absent » de « chaîne vide » : les huit
    /// champs sont vides aussi bien pour un producteur d'avant ce lot que pour une
    /// commande sans adresse. On retient le repère parce qu'il est OBLIGATOIRE
    /// côté domaine (`food_ordering.shipping_address_required`) : s'il est vide,
    /// il n'y a rien d'exploitable, quoi que portent les autres champs.
    /// </summary>
    private static Contracts.MealOrderShippingAddressSummary? LireAdresse(Proto.MealOrderView o)
        => string.IsNullOrEmpty(o.ShipToLandmark)
            ? null
            : new Contracts.MealOrderShippingAddressSummary(
                Recipient: string.IsNullOrEmpty(o.ShipToRecipient) ? null : o.ShipToRecipient,
                Phone: string.IsNullOrEmpty(o.ShipToPhone) ? null : o.ShipToPhone,
                CommuneName: string.IsNullOrEmpty(o.ShipToCommuneName) ? null : o.ShipToCommuneName,
                Quartier: string.IsNullOrEmpty(o.ShipToQuartier) ? null : o.ShipToQuartier,
                Landmark: o.ShipToLandmark,
                Line1: string.IsNullOrEmpty(o.ShipToLine1) ? null : o.ShipToLine1,
                Latitude: ParseNullableDouble(o.ShipToLatitude),
                Longitude: ParseNullableDouble(o.ShipToLongitude));

    /// <summary>
    /// Une coordonnée, ou <c>null</c>.
    ///
    /// NE JAMAIS RETOMBER SUR ZÉRO. `ParseDecimal`, plus bas, le fait pour les
    /// montants — « zéro franc » y est un montant plausible. Ici, 0/0 est un point
    /// réel du golfe de Guinée, à cinq cents kilomètres des côtes du Bénin : y
    /// envoyer un livreur serait pire que de refuser la course.
    /// </summary>
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

public static class FoodOrdersGrpcRegistration
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

        services.AddScoped<Contracts.IMealOrderModuleApi, FoodOrderGrpcClient>();

        return services;
    }
}
