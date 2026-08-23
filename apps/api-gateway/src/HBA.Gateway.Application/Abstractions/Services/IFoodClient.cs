using HBA.Gateway.Application.Contracts.Food;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>food-service</c> — restaurants, cartes, cuisine.</summary>
/// <remarks>
/// LES DEUX PREMIÈRES MÉTHODES N'EXISTAIENT PAS AVANT BFF-0.
///
/// food-service ne savait rendre qu'une carte dont on connaissait déjà
/// l'identifiant. Le BFF client HBA Food n'avait donc aucun amont : c'est ce
/// manque qui a fait décaler la phase 5 du plan.
/// </remarks>
public interface IFoodClient : IServiceClient
{
    /// <summary><c>GET /api/food/restaurants?page=&amp;pageSize=</c> — anonyme.</summary>
    Task<ServiceResult<IReadOnlyList<RestaurantCard>>> ListStorefrontAsync(
        int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /api/food/restaurants/{id}</c> — anonyme.
    /// Rend 404 pour un établissement hors vitrine, sans le distinguer d'un
    /// identifiant inexistant.
    /// </summary>
    Task<ServiceResult<RestaurantDetail>> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/food/restaurants/{id}/menu</c> — anonyme, existait déjà.</summary>
    Task<ServiceResult<RestaurantMenu>> GetMenuAsync(
        Guid restaurantId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /api/food/partner/me</c> — AUTHENTIFIÉ.
    /// Rend 404 quand le compte ne travaille dans aucun établissement.
    /// </summary>
    Task<ServiceResult<PartnerRestaurant>> GetMyRestaurantAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/food/partner/restaurants/{id}/kitchen</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<KitchenBoard>> GetKitchenAsync(
        Guid restaurantId, CancellationToken cancellationToken);
}
