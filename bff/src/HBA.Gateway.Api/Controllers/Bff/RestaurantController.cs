using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Restaurant;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de HBA Partner — activités HBA Food (§13, §14).</summary>
[ApiController]
[Route("api/v1/bff/restaurant")]
[Authorize(Policy = GatewayPolicies.RestaurantOnly)]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class RestaurantController : ControllerBase
{
    private readonly GetRestaurantDashboardHandler _dashboard;
    private readonly GetRestaurantKitchenHandler _kitchen;

    public RestaurantController(
        GetRestaurantDashboardHandler dashboard, GetRestaurantKitchenHandler kitchen)
    {
        _dashboard = dashboard;
        _kitchen = kitchen;
    }

    /// <summary>Tableau de bord du restaurant.</summary>
    [HttpGet("restaurants/{restaurantId:guid}/dashboard")]
    [ProducesResponseType<BffEnvelope<RestaurantDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<RestaurantDashboardDto>> GetDashboardAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => _dashboard.HandleAsync(restaurantId, cancellationToken);

    /// <summary>Écran de cuisine (KDS).</summary>
    [HttpGet("restaurants/{restaurantId:guid}/kitchen")]
    [ProducesResponseType<BffEnvelope<RestaurantKitchenDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<BffEnvelope<RestaurantKitchenDto>> GetKitchenAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => _kitchen.HandleAsync(restaurantId, cancellationToken);
}
