using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de l'application cliente — univers HBAExpress.</summary>
[ApiController]
[Route("api/v1/bff/client/express")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class ClientExpressController : ControllerBase
{
    private readonly GetExpressHomeHandler _home;
    private readonly GetProductDetailHandler _productDetail;

    public ClientExpressController(
        GetExpressHomeHandler home, GetProductDetailHandler productDetail)
    {
        _home = home;
        _productDetail = productDetail;
    }

    /// <summary>Accueil marketplace.</summary>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<ExpressHomeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<ExpressHomeDto>> GetHomeAsync(CancellationToken cancellationToken)
        => _home.HandleAsync(cancellationToken);

    /// <summary>Fiche produit.</summary>
    [HttpGet("products/{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<ProductDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<ProductDetailDto>> GetProductAsync(
        Guid productId, CancellationToken cancellationToken)
        => _productDetail.HandleAsync(productId, cancellationToken);
}
