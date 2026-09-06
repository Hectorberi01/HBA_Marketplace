using Contracts = HBA.Food.Contracts;
using Grpc.Core;
using HBA.Food.Contracts.Grpc;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Food.Grpc.V1;

using System.Runtime.CompilerServices;

using HBA.Food.Contracts;
// ═════════════════════════════════════════════════════════════════════════════
// DEPLACE DEPUIS `HBA.Food.Contracts.Grpc` (lot B de la migration gRPC).
//
// LE SERVEUR VIVAIT DANS L'ASSEMBLAGE DE CONTRATS, DONC CHEZ TOUS SES
// CONSOMMATEURS. Les dix services qui consomment merchant.proto liaient
// l'implementation de seller-service ; les huit qui consomment order.proto
// liaient celle d'order-service. Aucun ne s'en servait.
//
// Le serveur est la surface d'UN service : il vit desormais dans son `.Api`.
// L'assemblage de contrats ne porte plus que le stub genere, le client et son
// enregistrement — le lot C descendra ces deux-la chez les appelants.
//
// CE QUE ÇA NE CHANGE PAS : le cablage. `Program.cs` appelle toujours
// `MapInternalGrpcService<...>()`, avec la meme autorisation et les memes
// intercepteurs. Un deplacement de fichier ne rend rien plus sur.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Food.Api.Grpc.Services;

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
