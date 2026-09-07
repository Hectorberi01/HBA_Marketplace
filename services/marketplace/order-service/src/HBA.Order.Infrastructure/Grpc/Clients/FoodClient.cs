using Contracts = HBA.Food.Contracts;
using Grpc.Core;
using HBA.Food.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Food.Grpc.V1;

using System.Runtime.CompilerServices;

using ContratsFood = HBA.Food.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// COPIE DEPUIS `HBA.Food.Contracts.Grpc` (lot D — dissolution des assemblages de
// contrats).

namespace HBA.Orders.Infrastructure.Grpc.Clients;

internal sealed class FoodGrpcClient : ContratsFood.IFoodModuleApi
{
    private readonly Proto.FoodApi.FoodApiClient _client;

    public FoodGrpcClient(Proto.FoodApi.FoodApiClient client) => _client = client;

    public async Task<ContratsFood.RestaurantSummary?> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetRestaurantAsync(
            new Proto.GetRestaurantRequest { RestaurantId = restaurantId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Restaurant) : null;
    }

    // CES TROIS MÉTHODES RENDAIENT `null` EN DUR.

    public async Task<ContratsFood.RestaurantSummary?> GetRestaurantByOwnerAsync(
        Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetRestaurantByOwnerAsync(
            new Proto.GetRestaurantByOwnerRequest { OwnerUserId = ownerUserId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Restaurant) : null;
    }

    public async Task<ContratsFood.FoodStaffMembership?> GetStaffMembershipAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetStaffMembershipAsync(
            new Proto.GetStaffMembershipRequest { UserId = userId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found || response.Membership is null)
        {
            return null;
        }

        var m = response.Membership;

        return new ContratsFood.FoodStaffMembership(
            ParseGuid(m.RestaurantId),
            ParseGuid(m.StaffId),
            ParseGuid(m.UserId),
            m.Role,
            m.IsActive,
            m.IsFounder,
            m.Permissions.ToList());
    }

    public async Task<ContratsFood.FoodOrderRef?> GetOrderAsync(
        Guid foodOrderId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetFoodOrderAsync(
            new Proto.GetFoodOrderRequest { FoodOrderId = foodOrderId.ToString() },
            cancellationToken: cancellationToken);

        if (!response.Found || response.Order is null)
        {
            return null;
        }

        var o = response.Order;

        return new ContratsFood.FoodOrderRef(
            ParseGuid(o.FoodOrderId),
            ParseGuid(o.OrderId),
            ParseGuid(o.RestaurantId),
            o.Status,

            // Vide chez un producteur d'avant le lot 6.4 : on retombe alors sur «
            // Marketplace », qui décrit exactement les tickets de cette époque.
            string.IsNullOrEmpty(o.Origin)
                ? ContratsFood.IntegrationEvents.FoodOrderOrigins.Marketplace
                : o.Origin);
    }

    public async Task<ContratsFood.MenuItemView?> GetMenuItemAsync(
        Guid restaurantId, Guid menuItemId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetMenuItemAsync(
            new Proto.GetMenuItemRequest
            {
                RestaurantId = restaurantId.ToString(),
                MenuItemId = menuItemId.ToString()
            },
            cancellationToken: cancellationToken);

        if (!response.Found || response.Item is null)
        {
            return null;
        }

        var i = response.Item;

        return new ContratsFood.MenuItemView(
            Id: ParseGuid(i.MenuItemId),
            Name: i.Name,
            Description: null,
            ImageMediaId: null,
            LegacyImageUrl: null,
            DisplayImageUrl: i.HasImageUrl ? i.ImageUrl : null,
            BasePrice: ParseDecimal(i.BaseAmount),
            Currency: i.Currency,
            IsOrderable: i.IsOrderable,

            // `HasImage` SE DÉDUIT ICI DE L'ADRESSE, ET C'EST UNE APPROXIMATION.
            HasImage: i.HasImageUrl,
            BackAtUtc: null,
            OptionGroups: i.OptionGroups
                .Select(g => new ContratsFood.OptionGroupView(
                    ParseGuid(g.OptionGroupId),
                    g.Name,
                    g.MinSelections,
                    g.MaxSelections,
                    g.IsRequired,
                    g.Options
                        .Select(o => new ContratsFood.OptionView(
                            ParseGuid(o.OptionId), o.Name, ParseDecimal(o.PriceDelta), o.IsAvailable))
                        .ToList()))
                .ToList());
    }

    private static ContratsFood.RestaurantSummary ToContract(Proto.RestaurantSummary restaurant)
        => new(
            Id: ParseGuid(restaurant.RestaurantId),
            OwnerUserId: ParseGuid(restaurant.OwnerUserId),
            Name: restaurant.Name,
            Description: restaurant.HasDescription ? restaurant.Description : null,
            LogoMediaId: null,
            CoverMediaId: null,
            LegacyLogoUrl: null,
            Phone: restaurant.Phone,
            Status: restaurant.Status,
            AcceptsOrdersNow: string.Equals(restaurant.Status, "Open", StringComparison.OrdinalIgnoreCase)
                || string.Equals(restaurant.Status, "Active", StringComparison.OrdinalIgnoreCase),
            BlockedReason: string.Empty,
            PreparationMinutes: 0,
            AcceptanceMode: "Manual",
            MinimumOrderAmount: null,
            LoadLevel: "Normal",
            ExtraWaitMinutes: 0,
            SpecialClosureReason: null,
            FulfillmentLocationId: null,
            PayoutSellerId: null,
            ServiceHours: [],
            IsPubliclyVisible: string.Equals(restaurant.Status, "Active", StringComparison.OrdinalIgnoreCase));

    private static Guid ParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : Guid.Empty;

    /// <summary>INVARIANT DE CULTURE DES DEUX CÔTÉS, SANS QUOI LE MONTANT CHANGE.</summary>
    private static decimal ParseDecimal(
        string? value, [CallerArgumentExpression(nameof(value))] string champ = "")
        => MontantSurLeFil.Lire(value, champ);
}

internal static class FoodGrpcRegistration
{
    public static IServiceCollection AddFoodGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Food"]
            ?? throw new InvalidOperationException("Services:Food est absent.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        services
            .AddGrpcClient<Proto.FoodApi.FoodApiClient>(options =>
                options.Address = new UriBuilder(address) { Port = grpcPort }.Uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<ContratsFood.IFoodModuleApi, FoodGrpcClient>();

        return services;
    }
}
