using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Food;
using HBA.Gateway.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Client;

/// <summary>Façade BFF de l'application cliente — univers HBA Food.</summary>
/// <remarks>Symétrique d'<see cref="ExpressBffController"/> : voir sa remarque.</remarks>
[ApiController]
[Obsolete("Route héritée. La version typée viendra avec BFF-0 (food-service incomplet).")]
[Route("api/bff/client/food")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class FoodBffController : ControllerBase
{
    private readonly FoodHomeService _home;

    public FoodBffController(FoodHomeService home) => _home = home;

    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffHomeResponse>(StatusCodes.Status200OK)]
    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _home.GetHomeAsync(cancellationToken);
}
