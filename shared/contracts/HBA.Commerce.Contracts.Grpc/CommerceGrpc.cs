using System.Runtime.CompilerServices;
using System.Globalization;
using Grpc.Core;
using HBA.Commerce.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Contracts = HBA.Commerce.Contracts;

namespace HBA.Commerce.Contracts.Grpc;

/// <summary>
/// Côté commerce-service : sert le panier valorisé à qui sait présenter le
/// secret interne.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE CONTRAT EXISTE.
///
/// `PlaceOrderCommandHandler` lit le panier pour figer ses prix dans une
/// commande. Dans le monolithe, il appelait `ICartModuleApi` en mémoire. Une
/// fois Ordering et Cart séparés en deux services, l'interface était toujours
/// injectée et plus personne ne la fournissait : le conteneur d'order-service
/// refusait de démarrer.
///
/// C'EST UNE LECTURE SUR LE CHEMIN CRITIQUE.
///
/// Sans réponse de commerce-service, aucune commande ne peut être passée.
/// L'échéance de cinq secondes posée par `InternalCallClientInterceptor` vaut
/// donc ici comme ailleurs : un panier qui ne répond pas doit rendre un refus
/// franc plutôt que retenir la requête de l'acheteur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CommerceGrpcService : CommerceApi.CommerceApiBase
{
    private readonly Contracts.ICartModuleApi _carts;

    public CommerceGrpcService(Contracts.ICartModuleApi carts) => _carts = carts;

    public override async Task<GetCartResponse> GetActiveCart(
        GetActiveCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.BuyerId, out var buyerId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "buyer_id n'est pas un GUID."));
        }

        var cart = await _carts.GetActiveCartAsync(buyerId, context.CancellationToken);
        return Respond(cart);
    }

    public override async Task<GetCartResponse> GetCart(
        GetCartRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CartId, out var cartId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cart_id n'est pas un GUID."));
        }

        var cart = await _carts.GetCartAsync(cartId, context.CancellationToken);
        return Respond(cart);
    }

    // « PAS DE PANIER » N'EST PAS UNE ERREUR.
    //
    // `NotFound` obligerait l'appelant à rattraper une RpcException pour un cas
    // parfaitement normal — un acheteur qui n'a rien mis dans son panier. Le
    // drapeau `found` distingue « pas de panier » de « le service n'a pas
    // répondu », et seule la seconde situation mérite une exception.
    private static GetCartResponse Respond(Contracts.CartSummary? cart)
        => cart is null
            ? new GetCartResponse { Found = false }
            : new GetCartResponse { Found = true, Cart = ToProto(cart) };

    private static CartView ToProto(Contracts.CartSummary cart)
    {
        var view = new CartView
        {
            CartId = cart.CartId.ToString(),
            BuyerId = cart.BuyerId.ToString(),
            Status = cart.Status,
            Currency = cart.Currency,
            Kind = cart.Kind ?? string.Empty,
            Subtotal = Money(cart.Subtotal),
            TotalSellerDiscount = Money(cart.TotalSellerDiscount),
            TotalPlatformDiscount = Money(cart.TotalPlatformDiscount),
            GrandTotal = Money(cart.GrandTotal),
            PromotionCode = cart.PromotionCode ?? string.Empty
        };

        foreach (var line in cart.Lines)
        {
            var message = new CartLine
            {
                LineId = line.LineId.ToString(),
                Kind = line.Kind,
                OfferId = line.OfferId.ToString(),
                ProductId = line.ProductId.ToString(),
                CategoryId = line.CategoryId.ToString(),
                SellerId = line.SellerId.ToString(),
                Sku = line.Sku,
                ShipFromLocationId = line.ShipFromLocationId.ToString(),
                Quantity = line.Quantity,
                UnitBaseAmount = Money(line.UnitBaseAmount),
                SellerDiscount = Money(line.SellerDiscount),
                PlatformDiscount = Money(line.PlatformDiscount),
                FinalUnitPrice = Money(line.FinalUnitPrice),
                LineTotal = Money(line.LineTotal),
                Currency = line.Currency,
                RestaurantId = line.RestaurantId.ToString(),
                MenuItemId = line.MenuItemId.ToString(),
                Notes = line.Notes ?? string.Empty
            };

            if (line.Options is { Count: > 0 })
            {
                foreach (var option in line.Options)
                {
                    message.Options.Add(new CartLineOption
                    {
                        OptionGroupId = option.OptionGroupId.ToString(),
                        OptionId = option.OptionId.ToString()
                    });
                }
            }

            view.Lines.Add(message);
        }

        return view;
    }

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Côté order-service : `ICartModuleApi`, mais sur le réseau.</summary>
public sealed class CommerceGrpcClient : Contracts.ICartModuleApi
{
    private readonly CommerceApi.CommerceApiClient _client;

    public CommerceGrpcClient(CommerceApi.CommerceApiClient client) => _client = client;

    public async Task<Contracts.CartSummary?> GetActiveCartAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetActiveCartAsync(
            new GetActiveCartRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    public async Task<Contracts.CartSummary?> GetCartAsync(
        Guid cartId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetCartAsync(
            new GetCartRequest { CartId = cartId.ToString() },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    private static Contracts.CartSummary? FromProto(GetCartResponse response)
    {
        if (!response.Found || response.Cart is null)
        {
            return null;
        }

        var cart = response.Cart;

        var lines = cart.Lines
            .Select(line => new Contracts.CartLineSummary(
                ToGuid(line.LineId),
                line.Kind,
                ToGuid(line.OfferId),
                ToGuid(line.ProductId),
                ToGuid(line.CategoryId),
                ToGuid(line.SellerId),
                line.Sku,
                ToGuid(line.ShipFromLocationId),
                line.Quantity,
                Money(line.UnitBaseAmount),
                Money(line.SellerDiscount),
                Money(line.PlatformDiscount),
                Money(line.FinalUnitPrice),
                Money(line.LineTotal),
                line.Currency,
                ToGuid(line.RestaurantId),
                ToGuid(line.MenuItemId),

                // CHAÎNE VIDE ET NULL NE SE DISTINGUENT PAS EN PROTOBUF3.
                //
                // Un champ `string` absent arrive comme "". Pour des notes de
                // préparation, les deux veulent dire la même chose, et on rend
                // `null` pour ne pas fabriquer une note vide qui s'afficherait
                // en cuisine comme une consigne.
                string.IsNullOrEmpty(line.Notes) ? null : line.Notes,

                line.Options
                    .Select(option => new Contracts.CartLineOptionSummary(
                        ToGuid(option.OptionGroupId), ToGuid(option.OptionId)))
                    .ToList()))
            .ToList();

        return new Contracts.CartSummary(
            ToGuid(cart.CartId),
            ToGuid(cart.BuyerId),
            cart.Currency,
            cart.Status,
            string.IsNullOrEmpty(cart.Kind) ? null : cart.Kind,
            lines,
            Money(cart.Subtotal),
            Money(cart.TotalSellerDiscount),
            Money(cart.TotalPlatformDiscount),
            Money(cart.GrandTotal),
            string.IsNullOrEmpty(cart.PromotionCode) ? null : cart.PromotionCode);
    }

    // ON NE LÈVE PAS SUR UN CHAMP MAL FORMÉ, ON REND `Guid.Empty`.
    //
    // Les identifiants de l'autre nature sont toujours vides : une ligne de
    // marchandise n'a pas de restaurant. Lever ferait échouer tout le panier sur
    // un champ dont l'absence est normale.
    private static Guid ToGuid(string value)
        => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;

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
    private static decimal Money(
        string value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}

public static class CommerceGrpcRegistration
{
    public static IServiceCollection AddCommerceGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Commerce"]
            ?? throw new InvalidOperationException("Services:Commerce est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<CommerceApi.CommerceApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<Contracts.ICartModuleApi, CommerceGrpcClient>();

        return services;
    }
}
