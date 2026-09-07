using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Merchant;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

// LA POLITIQUE EST POSÉE PAR MÉTHODE, ET NON SUR LA CLASSE. NE PAS REMONTER.
/// <summary>Façade BFF de HBA Partner — boutiques HBAExpress (§11, §12, §44).</summary>
[ApiController]
[Route("api/v1/bff/merchant")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class MerchantController : ControllerBase
{
    private readonly GetMerchantActivitiesHandler _activities;
    private readonly GetMerchantDashboardHandler _dashboard;
    private readonly GetMerchantAnalyticsHandler _analytics;

    public MerchantController(
        GetMerchantActivitiesHandler activities,
        GetMerchantDashboardHandler dashboard,
        GetMerchantAnalyticsHandler analytics)
    {
        _activities = activities;
        _dashboard = dashboard;
        _analytics = analytics;
    }

    /// <summary>Les activités du compte : boutiques et restaurants.</summary>
    [HttpGet("activities")]
    [Authorize(Policy = GatewayPolicies.PartnerOnly)]
    [ProducesResponseType<BffEnvelope<MerchantActivitiesDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<BffEnvelope<MerchantActivitiesDto>> GetActivitiesAsync(
        CancellationToken cancellationToken)
        => _activities.HandleAsync(cancellationToken);

    /// <summary>Tableau de bord d'une boutique.</summary>
    [HttpGet("stores/{storeId:guid}/dashboard")]
    [Authorize(Policy = GatewayPolicies.MerchantOnly)]
    [ProducesResponseType<BffEnvelope<MerchantDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<MerchantDashboardDto>> GetStoreDashboardAsync(
        Guid storeId, CancellationToken cancellationToken)
        => _dashboard.HandleAsync(storeId, cancellationToken);

    /// <summary>Les courbes de vente du vendeur connecté.</summary>
    [HttpGet("analytics")]
    [Authorize(Policy = GatewayPolicies.MerchantOnly)]
    [ProducesResponseType<BffEnvelope<MerchantAnalyticsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<BffEnvelope<MerchantAnalyticsDto>> GetAnalyticsAsync(
        int? days, CancellationToken cancellationToken)
        => _analytics.HandleAsync(days, cancellationToken);
}
