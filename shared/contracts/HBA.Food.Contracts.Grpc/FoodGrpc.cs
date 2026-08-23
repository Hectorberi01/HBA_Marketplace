using System.Runtime.CompilerServices;
using Grpc.Core;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Contracts = HBA.Food.Contracts;
using Proto = HBA.Food.Grpc.V1;

namespace HBA.Food.Contracts.Grpc;

public sealed class FoodGrpcService : Proto.FoodApi.FoodApiBase
{
    private readonly Contracts.IFoodModuleApi _food;

    public FoodGrpcService(Contracts.IFoodModuleApi food) => _food = food;

    public override async Task<Proto.GetRestaurantResponse> GetRestaurant(
        Proto.GetRestaurantRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "restaurant_id n'est pas un GUID."));
        }

        var restaurant = await _food.GetRestaurantAsync(restaurantId, context.CancellationToken);
        return restaurant is null
            ? new Proto.GetRestaurantResponse { Found = false }
            : new Proto.GetRestaurantResponse { Found = true, Restaurant = ToProto(restaurant) };
    }

    public override async Task<Proto.GetRestaurantResponse> GetRestaurantByOwner(
        Proto.GetRestaurantByOwnerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.OwnerUserId, out var ownerUserId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "owner_user_id n'est pas un GUID."));
        }

        var restaurant = await _food.GetRestaurantByOwnerAsync(ownerUserId, context.CancellationToken);
        return restaurant is null
            ? new Proto.GetRestaurantResponse { Found = false }
            : new Proto.GetRestaurantResponse { Found = true, Restaurant = ToProto(restaurant) };
    }

    public override async Task<Proto.GetStaffMembershipResponse> GetStaffMembership(
        Proto.GetStaffMembershipRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "user_id n'est pas un GUID."));
        }

        var membre = await _food.GetStaffMembershipAsync(userId, context.CancellationToken);

        if (membre is null)
        {
            return new Proto.GetStaffMembershipResponse { Found = false };
        }

        var message = new Proto.StaffMembership
        {
            RestaurantId = membre.RestaurantId.ToString(),
            StaffId = membre.StaffId.ToString(),
            UserId = membre.UserId.ToString(),
            Role = membre.Role,
            IsActive = membre.IsActive,
            IsFounder = membre.IsFounder
        };

        // LES PERMISSIONS VOYAGENT, ELLES NE SE DÉDUISENT PAS DU RÔLE.
        //
        // Un appelant qui recalculerait « ce que peut faire un cuisinier » à
        // partir du rôle recopierait une règle qui appartient à food-service, et
        // qui deviendrait fausse au premier rôle ajouté.
        message.Permissions.AddRange(membre.Permissions);

        return new Proto.GetStaffMembershipResponse { Found = true, Membership = message };
    }

    public override async Task<Proto.GetFoodOrderResponse> GetFoodOrder(
        Proto.GetFoodOrderRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.FoodOrderId, out var foodOrderId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "food_order_id n'est pas un GUID."));
        }

        var ticket = await _food.GetOrderAsync(foodOrderId, context.CancellationToken);

        return ticket is null
            ? new Proto.GetFoodOrderResponse { Found = false }
            : new Proto.GetFoodOrderResponse
            {
                Found = true,
                Order = new Proto.FoodOrderRef
                {
                    FoodOrderId = ticket.FoodOrderId.ToString(),
                    OrderId = ticket.OrderId.ToString(),
                    RestaurantId = ticket.RestaurantId.ToString(),
                    Status = ticket.Status,
                    Origin = ticket.Origin
                }
            };
    }

    /// <summary>
    /// CETTE MÉTHODE ÉTAIT DÉCLARÉE DANS LE `.proto` ET N'AVAIT AUCUN CORPS.
    ///
    /// Elle rendait donc `UNIMPLEMENTED` à qui l'appelait. Personne ne l'appelait
    /// : le panier des repas vivait dans cart-service, qui n'avait pas de client
    /// Food du tout, et se contentait du prix envoyé par le client. C'est
    /// exactement le trou que food-cart-service ferme — et il a besoin d'elle.
    /// </summary>
    public override async Task<Proto.GetMenuItemResponse> GetMenuItem(
        Proto.GetMenuItemRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.RestaurantId, out var restaurantId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "restaurant_id n'est pas un GUID."));
        }

        if (!Guid.TryParse(request.MenuItemId, out var menuItemId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "menu_item_id n'est pas un GUID."));
        }

        var article = await _food.GetMenuItemAsync(restaurantId, menuItemId, context.CancellationToken);

        if (article is null)
        {
            return new Proto.GetMenuItemResponse { Found = false };
        }

        var message = new Proto.MenuItemSummary
        {
            MenuItemId = article.Id.ToString(),
            RestaurantId = restaurantId.ToString(),
            Name = article.Name,
            Status = article.IsOrderable ? "Orderable" : "Unavailable",

            // INVARIANT DE CULTURE, ET PAS SEULEMENT « JOLI ».
            //
            // Sérialisé sous une culture française, 1500,50 s'écrit avec une
            // virgule ; relu sous une culture anglaise, il devient 150 050. Le
            // montant traverse un réseau : les deux côtés n'ont aucune raison
            // d'avoir la même culture.
            BaseAmount = article.BasePrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Currency = article.Currency,
            IsOrderable = article.IsOrderable
        };

        if (article.DisplayImageUrl is not null)
        {
            message.ImageUrl = article.DisplayImageUrl;
        }

        foreach (var groupe in article.OptionGroups)
        {
            var g = new Proto.OptionGroupSummary
            {
                OptionGroupId = groupe.Id.ToString(),
                Name = groupe.Name,
                MinSelections = groupe.MinSelections,
                MaxSelections = groupe.MaxSelections,
                IsRequired = groupe.IsRequired
            };

            foreach (var option in groupe.Options)
            {
                g.Options.Add(new Proto.OptionSummary
                {
                    OptionId = option.Id.ToString(),
                    Name = option.Name,
                    PriceDelta = option.PriceDelta.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    IsAvailable = option.IsAvailable
                });
            }

            message.OptionGroups.Add(g);
        }

        return new Proto.GetMenuItemResponse { Found = true, Item = message };
    }

    private static Proto.RestaurantSummary ToProto(Contracts.RestaurantSummary restaurant)
    {
        var message = new Proto.RestaurantSummary
        {
            RestaurantId = restaurant.Id.ToString(),
            OwnerUserId = restaurant.OwnerUserId.ToString(),
            Name = restaurant.Name,
            Status = restaurant.Status,
            Phone = restaurant.Phone
        };

        if (restaurant.Description is not null)
        {
            message.Description = restaurant.Description;
        }

        return message;
    }
}

public sealed class FoodGrpcClient : Contracts.IFoodModuleApi
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

public static class FoodGrpcRegistration
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
