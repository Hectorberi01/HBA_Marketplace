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

/// <summary>
/// Le service gRPC exposé par food-cart-service.
///
/// SANS `MapInternalGrpcService&lt;FoodCartGrpcService&gt;()` DANS `Program`,
/// food-order-service NE PEUT PAS LIRE LE PANIER.
///
/// Le client existe de l'autre côté, la configuration pointe la bonne adresse,
/// et l'appel rend `UNIMPLEMENTED`. Le symptôme apparaît au premier passage en
/// commande, pas au démarrage — la même erreur a déjà été faite côté commerce,
/// et le commentaire y est encore.
/// </summary>
public sealed class FoodCartGrpcService : Proto.FoodCartApi.FoodCartApiBase
{
    private readonly Contracts.IFoodCartModuleApi _carts;

    public FoodCartGrpcService(Contracts.IFoodCartModuleApi carts) => _carts = carts;

    public override async Task<Proto.GetFoodCartResponse> GetActiveCart(
        Proto.GetActiveFoodCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "buyer_id n'est pas un GUID."));
        }

        var cart = await _carts.GetActiveCartAsync(buyerId, context.CancellationToken);
        return Repondre(cart);
    }

    public override async Task<Proto.GetFoodCartResponse> GetCart(
        Proto.GetFoodCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CartId, out var cartId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cart_id n'est pas un GUID."));
        }

        var cart = await _carts.GetCartAsync(cartId, context.CancellationToken);
        return Repondre(cart);
    }

    /// <summary>
    /// UN PANIER SANS IDENTIFIANT EST UN PANIER QUI N'EXISTE PAS.
    ///
    /// `GetActiveCartQuery` rend un panier VIDE plutôt qu'une erreur, pour que
    /// l'écran affiche « votre panier est vide ». Le rendre tel quel par le
    /// réseau ferait croire à food-order-service qu'il tient un panier — de
    /// `CartId` nul, sans restaurant, sans ligne — et la commande partirait à
    /// zéro franc au lieu d'être refusée.
    /// </summary>
    private static Proto.GetFoodCartResponse Repondre(Contracts.FoodCartSummary? cart)
    {
        if (cart is null || cart.CartId == Guid.Empty)
        {
            return new Proto.GetFoodCartResponse { Found = false };
        }

        var vue = new Proto.FoodCartView
        {
            CartId = cart.CartId.ToString(),
            BuyerId = cart.BuyerId.ToString(),
            RestaurantId = (cart.RestaurantId ?? Guid.Empty).ToString(),
            Status = cart.Status,
            Currency = cart.Currency,
            Subtotal = Ecrire(cart.Subtotal),
            TotalSellerDiscount = Ecrire(cart.TotalSellerDiscount),
            TotalPlatformDiscount = Ecrire(cart.TotalPlatformDiscount),
            GrandTotal = Ecrire(cart.GrandTotal),
            PromotionCode = cart.PromotionCode ?? string.Empty
        };

        foreach (var ligne in cart.Lines)
        {
            var l = new Proto.FoodCartLine
            {
                LineId = ligne.LineId.ToString(),
                MenuItemId = ligne.MenuItemId.ToString(),
                Name = ligne.Name,
                Quantity = ligne.Quantity,
                UnitBaseAmount = Ecrire(ligne.UnitBaseAmount),
                SellerDiscount = Ecrire(ligne.SellerDiscount),
                PlatformDiscount = Ecrire(ligne.PlatformDiscount),
                FinalUnitPrice = Ecrire(ligne.FinalUnitPrice),
                LineTotal = Ecrire(ligne.LineTotal),
                Currency = ligne.Currency,
                Notes = ligne.Notes ?? string.Empty
            };

            foreach (var option in ligne.Options)
            {
                l.Options.Add(new Proto.FoodCartLineOption
                {
                    OptionGroupId = option.OptionGroupId.ToString(),
                    OptionId = option.OptionId.ToString()
                });
            }

            vue.Lines.Add(l);
        }

        return new Proto.GetFoodCartResponse { Found = true, Cart = vue };
    }

    private static string Ecrire(decimal montant)
        => montant.ToString(CultureInfo.InvariantCulture);
}

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
