using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Admin;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF du back-office.</summary>
[ApiController]
[Route("api/v1/bff/admin")]
[Authorize(Policy = GatewayPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class AdminController : ControllerBase
{
    private readonly GetAdminQueuesHandler _queues;
    private readonly GetAdminAnalyticsHandler _analytics;

    public AdminController(GetAdminQueuesHandler queues, GetAdminAnalyticsHandler analytics)
    {
        _queues = queues;
        _analytics = analytics;
    }

    /// <summary>Les files d'attente d'administration, en un seul appel.</summary>
    [HttpGet("queues")]
    [ProducesResponseType<BffEnvelope<AdminQueuesDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<BffEnvelope<AdminQueuesDto>> GetQueuesAsync(CancellationToken cancellationToken)
        => _queues.HandleAsync(cancellationToken);

    /// <summary>Les courbes de la plateforme : activité et inscriptions.</summary>
    [HttpGet("analytics")]
    [ProducesResponseType<BffEnvelope<AdminAnalyticsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<BffEnvelope<AdminAnalyticsDto>> GetAnalyticsAsync(
        int? days, CancellationToken cancellationToken)
        => _analytics.HandleAsync(days, cancellationToken);
}
