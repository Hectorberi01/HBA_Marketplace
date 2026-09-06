using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Contracts = HBA.FoodCarts.Contracts;
using Proto = HBA.FoodCarts.Grpc.V1;

namespace HBA.FoodCarts.Contracts.Grpc;



public sealed class FoodCartGrpcClient : Contracts.IFoodCartModuleApi
{
    private readonly Proto.FoodCartApi.FoodCartApiClient _client;

    public FoodCartGrpcClient(Proto.FoodCartApi.FoodCartApiClient client) => _client = client;

    public async Task<Contracts.FoodCartSummary?> GetActiveCartAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.GetActiveCartAsync(
            new Proto.GetActiveFoodCartRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Found ? Lire(reponse.Cart) : null;
    }

    public async Task<Contracts.FoodCartSummary?> GetCartAsync(
        Guid cartId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.GetCartAsync(
            new Proto.GetFoodCartRequest { CartId = cartId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Found ? Lire(reponse.Cart) : null;
    }

    private static Contracts.FoodCartSummary Lire(Proto.FoodCartView vue)
        => new(
            CartId: ParseGuid(vue.CartId),
            BuyerId: ParseGuid(vue.BuyerId),
            RestaurantId: ParseGuid(vue.RestaurantId),
            Currency: vue.Currency,
            Status: vue.Status,
            Lines: vue.Lines
                .Select(l => new Contracts.FoodCartLineSummary(
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
                        .Select(o => new Contracts.FoodCartLineOptionSummary(
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
}

public static class FoodCartsGrpcRegistration
{
    public static IServiceCollection AddFoodCartsGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        // IL JETTE À LA CONSTRUCTION DE L'HÔTE, ET C'EST VOULU.
        //
        // Une adresse absente ne doit pas produire un client qui échoue au
        // premier appel, des heures plus tard, sur un chemin de paiement. Elle
        // doit empêcher le service de démarrer.
        var address = configuration["Services:FoodCart"]
            ?? throw new InvalidOperationException("Services:FoodCart est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.FoodCartApi.FoodCartApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<Contracts.IFoodCartModuleApi, FoodCartGrpcClient>();

        return services;
    }
}
