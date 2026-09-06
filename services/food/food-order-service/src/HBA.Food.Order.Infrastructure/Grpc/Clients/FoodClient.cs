using Contracts = HBA.Food.Contracts;
using Grpc.Core;
using HBA.Food.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Food.Grpc.V1;

using System.Runtime.CompilerServices;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Food.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
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
// CE QUE ÇA COUTE : cette traduction existe en 5 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.FoodOrders.Infrastructure.Grpc.Clients;

internal sealed class FoodGrpcClient : Contracts.IFoodModuleApi
{
    private readonly Proto.FoodApi.FoodApiClient _client;

    public FoodGrpcClient(Proto.FoodApi.FoodApiClient client) => _client = client;

    public async Task<Contracts.RestaurantSummary?> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetRestaurantAsync(
            new Proto.GetRestaurantRequest { RestaurantId = restaurantId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Restaurant) : null;
    }

    // CES TROIS MÉTHODES RENDAIENT `null` EN DUR.
    //
    // Elles satisfaisaient l'interface sans jamais contacter personne. Sans
    // effet tant que leurs seuls appelants vivaient dans food-service, qui
    // utilise l'implémentation en processus — mais elles auraient menti au
    // premier service les appelant par le réseau, et financial référence déjà
    // ce client.
    //
    // Un bouchon silencieux ne se découvre qu'en regardant une donnée absente.

    public async Task<Contracts.RestaurantSummary?> GetRestaurantByOwnerAsync(
        Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetRestaurantByOwnerAsync(
            new Proto.GetRestaurantByOwnerRequest { OwnerUserId = ownerUserId.ToString() },
            cancellationToken: cancellationToken);

        return response.Found ? ToContract(response.Restaurant) : null;
    }

    public async Task<Contracts.FoodStaffMembership?> GetStaffMembershipAsync(
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

        return new Contracts.FoodStaffMembership(
            ParseGuid(m.RestaurantId),
            ParseGuid(m.StaffId),
            ParseGuid(m.UserId),
            m.Role,
            m.IsActive,
            m.IsFounder,
            m.Permissions.ToList());
    }

    public async Task<Contracts.FoodOrderRef?> GetOrderAsync(
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

        return new Contracts.FoodOrderRef(
            ParseGuid(o.FoodOrderId),
            ParseGuid(o.OrderId),
            ParseGuid(o.RestaurantId),
            o.Status,

            // Vide chez un producteur d'avant le lot 6.4 : on retombe alors sur
            // « Marketplace », qui décrit exactement les tickets de cette époque.
            string.IsNullOrEmpty(o.Origin)
                ? Contracts.IntegrationEvents.FoodOrderOrigins.Marketplace
                : o.Origin);
    }

    public async Task<Contracts.MenuItemView?> GetMenuItemAsync(
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

        return new Contracts.MenuItemView(
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
            //
            // Le contrat distingue « porte une photo » d'« a une adresse
            // affichable » — un média repris dont le bucket a été renommé rend les
            // deux contradictoires, et c'est ce cas-là qu'on veut diagnostiquer.
            // Le transport ne porte que l'adresse. L'appelant de ce client est le
            // panier, qui n'a rien à faire de la photo : la nuance n'a de valeur
            // que dans l'espace restaurateur, qui lit l'implémentation en
            // processus. Étendre le message pour un consommateur qui n'existe pas
            // coûterait plus qu'il ne rapporte.
            HasImage: i.HasImageUrl,
            BackAtUtc: null,
            OptionGroups: i.OptionGroups
                .Select(g => new Contracts.OptionGroupView(
                    ParseGuid(g.OptionGroupId),
                    g.Name,
                    g.MinSelections,
                    g.MaxSelections,
                    g.IsRequired,
                    g.Options
                        .Select(o => new Contracts.OptionView(
                            ParseGuid(o.OptionId), o.Name, ParseDecimal(o.PriceDelta), o.IsAvailable))
                        .ToList()))
                .ToList());
    }

    private static Contracts.RestaurantSummary ToContract(Proto.RestaurantSummary restaurant)
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

    /// <summary>
    /// INVARIANT DE CULTURE DES DEUX CÔTÉS, SANS QUOI LE MONTANT CHANGE.
    ///
    /// L'émetteur écrit « 1500.50 ». Un lecteur sous culture française lirait
    /// 150050 — une erreur d'un facteur cent, silencieuse, sur un prix.
    /// </summary>
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

        services.AddScoped<Contracts.IFoodModuleApi, FoodGrpcClient>();

        return services;
    }
}
