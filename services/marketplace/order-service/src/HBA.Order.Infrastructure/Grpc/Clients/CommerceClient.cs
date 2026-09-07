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

using ContratsCommerce = HBA.Commerce.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Commerce.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Orders.Infrastructure.Grpc.Clients;

/// <summary>Côté order-service : `ICartModuleApi`, mais sur le réseau.</summary>
internal sealed class CommerceGrpcClient : ContratsCommerce.ICartModuleApi
{
    private readonly CommerceApi.CommerceApiClient _client;

    public CommerceGrpcClient(CommerceApi.CommerceApiClient client) => _client = client;

    public async Task<ContratsCommerce.CartSummary?> GetActiveCartAsync(
        Guid buyerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetActiveCartAsync(
            new GetActiveCartRequest { BuyerId = buyerId.ToString() },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    public async Task<ContratsCommerce.CartSummary?> GetCartAsync(
        Guid cartId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetCartAsync(
            new GetCartRequest { CartId = cartId.ToString() },
            cancellationToken: cancellationToken);

        return FromProto(response);
    }

    private static ContratsCommerce.CartSummary? FromProto(GetCartResponse response)
    {
        if (!response.Found || response.Cart is null)
        {
            return null;
        }

        var cart = response.Cart;

        var lines = cart.Lines
            .Select(line => new ContratsCommerce.CartLineSummary(
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
                string.IsNullOrEmpty(line.Notes) ? null : line.Notes,

                line.Options
                    .Select(option => new ContratsCommerce.CartLineOptionSummary(
                        ToGuid(option.OptionGroupId), ToGuid(option.OptionId)))
                    .ToList()))
            .ToList();

        return new ContratsCommerce.CartSummary(
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
    private static Guid ToGuid(string value)
        => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;

    /// <summary>Un montant venu du fil.</summary>
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

        services.AddScoped<ContratsCommerce.ICartModuleApi, CommerceGrpcClient>();

        return services;
    }
}
