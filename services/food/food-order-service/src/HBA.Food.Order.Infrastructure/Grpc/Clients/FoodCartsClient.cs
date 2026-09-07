using Contracts = HBA.FoodCarts.Contracts;
using Grpc.Core;
using HBA.FoodCarts.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.FoodCarts.Grpc.V1;

using System.Globalization;
using System.Runtime.CompilerServices;

using ContratsFoodCarts = HBA.FoodCarts.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.FoodCarts.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.FoodOrders.Infrastructure.Grpc.Clients;

internal sealed class FoodCartGrpcClient : ContratsFoodCarts.IFoodCartModuleApi
{
    private readonly Proto.FoodCartApi.FoodCartApiClient _client;

    public FoodCartGrpcClient(Proto.FoodCartApi.FoodCartApiClient client) => _client = client;

    public async Task<ContratsFoodCarts.FoodCartSummary?> GetActiveCartAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.GetActiveCartAsync(
            new Proto.GetActiveFoodCartRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Found ? Lire(reponse.Cart) : null;
    }

    public async Task<ContratsFoodCarts.FoodCartSummary?> GetCartAsync(
        Guid cartId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.GetCartAsync(
            new Proto.GetFoodCartRequest { CartId = cartId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Found ? Lire(reponse.Cart) : null;
    }

    private static ContratsFoodCarts.FoodCartSummary Lire(Proto.FoodCartView vue)
        => new(
            CartId: ParseGuid(vue.CartId),
            BuyerId: ParseGuid(vue.BuyerId),
            RestaurantId: ParseGuid(vue.RestaurantId),
            Currency: vue.Currency,
            Status: vue.Status,
            Lines: vue.Lines
                .Select(l => new ContratsFoodCarts.FoodCartLineSummary(
                    ParseGuid(l.LineId),
                    ParseGuid(l.MenuItemId),
                    l.Name,
                    l.Quantity,
                    ParseDecimal(l.UnitBaseAmount),
                    ParseDecimal(l.SellerDiscount),
                    ParseDecimal(l.PlatformDiscount),
                    ParseDecimal(l.FinalUnitPrice),
                    ParseDecimal(l.LineTotal),
                    l.Currency,
                    string.IsNullOrEmpty(l.Notes) ? null : l.Notes,
                    l.Options
                        .Select(o => new ContratsFoodCarts.FoodCartLineOptionSummary(
                            ParseGuid(o.OptionGroupId), ParseGuid(o.OptionId)))
                        .ToList()))
                .ToList(),
            Subtotal: ParseDecimal(vue.Subtotal),
            TotalSellerDiscount: ParseDecimal(vue.TotalSellerDiscount),
            TotalPlatformDiscount: ParseDecimal(vue.TotalPlatformDiscount),
            GrandTotal: ParseDecimal(vue.GrandTotal),
            PromotionCode: string.IsNullOrEmpty(vue.PromotionCode) ? null : vue.PromotionCode);

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    /// <summary>Un montant venu du fil.</summary>
    private static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}

internal static class FoodCartsGrpcRegistration
{
    public static IServiceCollection AddFoodCartsGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        // IL JETTE À LA CONSTRUCTION DE L'HÔTE, ET C'EST VOULU.
        var address = configuration["Services:FoodCart"]
            ?? throw new InvalidOperationException("Services:FoodCart est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.FoodCartApi.FoodCartApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<ContratsFoodCarts.IFoodCartModuleApi, FoodCartGrpcClient>();

        return services;
    }
}
