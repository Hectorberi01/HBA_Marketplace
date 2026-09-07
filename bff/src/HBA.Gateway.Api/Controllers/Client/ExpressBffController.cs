using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Client;

/// <summary>Façade BFF de l'application cliente — univers HBAExpress.</summary>
[ApiController]
// ROUTE HÉRITÉE, CONSERVÉE LE TEMPS DE LA BASCULE DES CLIENTS.
[Obsolete("Utiliser /api/v1/bff/client/express/home. Retrait prévu après bascule des clients.")]
[Route("api/bff/client/express")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class ExpressBffController : ControllerBase
{
    private readonly ExpressHomeService _home;

    public ExpressBffController(ExpressHomeService home) => _home = home;

    /// <summary>Accueil marketplace, agrégé à partir des sections configurées.</summary>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffHomeResponse>(StatusCodes.Status200OK)]
    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _home.GetHomeAsync(cancellationToken);
}
