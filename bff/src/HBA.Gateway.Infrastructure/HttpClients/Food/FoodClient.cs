using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Food;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Food;

/// <inheritdoc cref="IFoodClient" />
public sealed class FoodClient : ServiceHttpClient, IFoodClient
{
    public FoodClient(HttpClient http, ILogger<FoodClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Food;

    public Task<ServiceResult<IReadOnlyList<RestaurantCard>>> ListStorefrontAsync(
        int page, int pageSize, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<RestaurantCard>>(
            $"/api/food/restaurants?page={page}&pageSize={pageSize}", cancellationToken);

    public Task<ServiceResult<RestaurantDetail>> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => GetAsync<RestaurantDetail>(
            $"/api/food/restaurants/{restaurantId}", cancellationToken);

    public Task<ServiceResult<RestaurantMenu>> GetMenuAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => GetAsync<RestaurantMenu>(
            $"/api/food/restaurants/{restaurantId}/menu", cancellationToken);

    public Task<ServiceResult<PartnerRestaurant>> GetMyRestaurantAsync(
        CancellationToken cancellationToken)
        => GetAsync<PartnerRestaurant>("/api/food/partner/me", cancellationToken);

    public Task<ServiceResult<KitchenBoard>> GetKitchenAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => GetAsync<KitchenBoard>(
            $"/api/food/partner/restaurants/{restaurantId}/kitchen", cancellationToken);
}
