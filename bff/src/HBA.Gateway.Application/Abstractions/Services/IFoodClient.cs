using HBA.Gateway.Application.Contracts.Food;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>food-service</c> — restaurants, cartes, cuisine.</summary>
public interface IFoodClient : IServiceClient
{
    /// <summary><c>GET /api/food/restaurants?page=&amp;pageSize=</c> — anonyme.</summary>
    Task<ServiceResult<IReadOnlyList<RestaurantCard>>> ListStorefrontAsync(
        int page, int pageSize, CancellationToken cancellationToken);

    /// <summary><c>GET /api/food/restaurants/{id}</c> — anonyme.</summary>
    Task<ServiceResult<RestaurantDetail>> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/food/restaurants/{id}/menu</c> — anonyme, existait déjà.</summary>
    Task<ServiceResult<RestaurantMenu>> GetMenuAsync(
        Guid restaurantId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/food/partner/me</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<PartnerRestaurant>> GetMyRestaurantAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/food/partner/restaurants/{id}/kitchen</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<KitchenBoard>> GetKitchenAsync(
        Guid restaurantId, CancellationToken cancellationToken);
}
