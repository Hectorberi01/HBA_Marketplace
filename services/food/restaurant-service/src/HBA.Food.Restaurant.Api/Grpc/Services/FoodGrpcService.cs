using Contracts = HBA.Food.Contracts;
using Grpc.Core;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Proto = HBA.Food.Grpc.V1;

using System.Runtime.CompilerServices;

using HBA.Food.Contracts;
using ContratsFood = HBA.Food.Contracts;  // alias non masquable : voir tools/migration-grpc/lot_d_resolution.py
// DEPLACE DEPUIS `HBA.Food.Contracts.Grpc` (lot B de la migration gRPC).

namespace HBA.Food.Api.Grpc.Services;

internal sealed class FoodGrpcService : Proto.FoodApi.FoodApiBase
{
    private readonly ContratsFood.IFoodModuleApi _food;

    public FoodGrpcService(ContratsFood.IFoodModuleApi food) => _food = food;

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

    /// <summary>CETTE MÉTHODE ÉTAIT DÉCLARÉE DANS LE `.proto` ET N'AVAIT AUCUN CORPS.</summary>
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

    private static Proto.RestaurantSummary ToProto(ContratsFood.RestaurantSummary restaurant)
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
