using Contracts = HBA.Commerce.Contracts;

using Grpc.Core;
using HBA.Commerce.Contracts;
using HBA.Commerce.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System.Globalization;
using System.Runtime.CompilerServices;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Commerce.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
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
// CE QUE ÇA COUTE : cette traduction existe en 2 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Orders.Infrastructure.Grpc.Clients;

/// <summary>Côté order-service : `ICartModuleApi`, mais sur le réseau.</summary>
internal sealed class CommerceGrpcClient : Contracts.ICartModuleApi
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

internal static class CommerceGrpcRegistration
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
