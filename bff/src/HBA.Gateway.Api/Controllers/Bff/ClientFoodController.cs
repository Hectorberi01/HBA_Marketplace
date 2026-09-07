using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Food;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de l'application cliente — univers HBA Food.</summary>
[ApiController]
[Route("api/v1/bff/client/food")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class ClientFoodController : ControllerBase
{
    private readonly GetFoodHomeHandler _home;
    private readonly GetRestaurantDetailHandler _detail;

    public ClientFoodController(GetFoodHomeHandler home, GetRestaurantDetailHandler detail)
    {
        _home = home;
        _detail = detail;
    }

    /// <summary>Vitrine des restaurants.</summary>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<FoodHomeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<FoodHomeDto>> GetHomeAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        // Les bornes sont appliquées par `PageRequest`, jamais laissées au service
        // amont : `pageSize=100000` doit être ramené AVANT de partir.
        => _home.HandleAsync(new PageRequest(page, pageSize), cancellationToken);

    /// <summary>Fiche d'un restaurant, carte comprise.</summary>
    [HttpGet("restaurants/{restaurantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<FoodRestaurantDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<FoodRestaurantDetailDto>> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => _detail.HandleAsync(restaurantId, cancellationToken);
}
