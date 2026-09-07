using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Driver;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de l'application livreur (§15, §16).</summary>
[ApiController]
[Route("api/v1/bff/driver")]
[Authorize(Policy = GatewayPolicies.DriverOnly)]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class DriverController : ControllerBase
{
    private readonly GetDriverDashboardHandler _dashboard;
    private readonly GetDriverMissionsHandler _missions;
    private readonly GetDriverEarningsHandler _earnings;

    public DriverController(
        GetDriverDashboardHandler dashboard,
        GetDriverMissionsHandler missions,
        GetDriverEarningsHandler earnings)
    {
        _dashboard = dashboard;
        _missions = missions;
        _earnings = earnings;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType<BffEnvelope<DriverDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<DriverDashboardDto>> GetDashboardAsync(CancellationToken cancellationToken)
        => _dashboard.HandleAsync(cancellationToken);

    /// <summary>Missions du livreur.</summary>
    /// <param name="activeOnly">Ne rendre que les missions en cours.</param>
    [HttpGet("missions")]
    [ProducesResponseType<BffEnvelope<PagedResult<DriverMissionDto>>>(StatusCodes.Status200OK)]
    public Task<BffEnvelope<PagedResult<DriverMissionDto>>> GetMissionsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
        => _missions.ListAsync(new PageRequest(page, pageSize), activeOnly, cancellationToken);

    /// <summary>Détail d'une mission.</summary>
    [HttpGet("missions/{deliveryId:guid}")]
    [ProducesResponseType<BffEnvelope<DriverMissionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<BffEnvelope<DriverMissionDto>> GetMissionAsync(
        Guid deliveryId, CancellationToken cancellationToken)
        => _missions.GetAsync(deliveryId, cancellationToken);

    [HttpGet("earnings")]
    [ProducesResponseType<BffEnvelope<DriverEarningsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<DriverEarningsDto>> GetEarningsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        => _earnings.HandleAsync(new PageRequest(page, pageSize), cancellationToken);

    /// <summary>Profil du livreur.</summary>
    [HttpGet("profile")]
    [ProducesResponseType<BffEnvelope<DriverProfileDto>>(StatusCodes.Status200OK)]
    public async Task<BffEnvelope<DriverProfileDto>> GetProfileAsync(CancellationToken cancellationToken)
    {
        var dashboard = await _dashboard.HandleAsync(cancellationToken);
        return new BffEnvelope<DriverProfileDto>(dashboard.Data.Driver, dashboard.Warnings);
    }
}
