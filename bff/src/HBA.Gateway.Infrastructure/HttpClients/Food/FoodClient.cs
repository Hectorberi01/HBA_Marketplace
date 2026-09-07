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

    /// <remarks>
    /// `page` ET `pageSize` SONT DES ENTIERS, DONC SANS RISQUE D'INJECTION —
    ///    ET C'EST LA SEULE RAISON DE NE PAS LES ÉCHAPPER.
    ///
    /// Toute valeur de chaîne venant du client doit passer par
    /// <c>Uri.EscapeDataString</c> avant d'entrer dans un chemin ou une requête,
    /// comme le fait <c>InventoryClient</c> pour le SKU. Ici le type garantit
    /// déjà la forme.
    /// </remarks>
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
