using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Driver;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de l'application livreur (§15, §16).</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `DriverOnly` AU NIVEAU DU CONTRÔLEUR, ET AUCUNE ROUTE ANONYME.
///
/// À la différence des BFF client, rien ici n'est consultable sans session : une
/// mission porte le nom, le téléphone et l'adresse exacte d'un client. La
/// politique est donc posée sur la CLASSE — un `[AllowAnonymous]` ajouté par
/// distraction sur une action se verrait ; une politique oubliée sur une nouvelle
/// action ne se verrait pas.
///
/// AUCUN `driverId` N'EST ACCEPTÉ EN PARAMÈTRE, NULLE PART.
///
/// Le compte livreur se résout depuis le jeton, côté service. C'est ce qui
/// empêche un livreur d'ouvrir le tableau de bord, les gains ou les missions d'un
/// autre — la faille exacte que `ResolveDriverQuery` a fermée en amont, et qu'un
/// paramètre de route rouvrirait ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
    /// <param name="activeOnly">
    /// Ne rendre que les missions en cours. Par défaut <c>false</c> : l'écran
    /// « Missions » a quatre onglets, dont trois portent sur des courses closes.
    /// </param>
    [HttpGet("missions")]
    [ProducesResponseType<BffEnvelope<PagedResult<DriverMissionDto>>>(StatusCodes.Status200OK)]
    public Task<BffEnvelope<PagedResult<DriverMissionDto>>> GetMissionsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
        => _missions.ListAsync(new PageRequest(page, pageSize), activeOnly, cancellationToken);

    /// <summary>Détail d'une mission.</summary>
    /// <remarks>
    /// 404 si la mission n'appartient pas au livreur connecté — cf.
    /// <c>GetDriverMissionsHandler.GetAsync</c>. Un 403 confirmerait qu'elle
    /// existe et appartient à quelqu'un d'autre.
    /// </remarks>
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
    /// <remarks>
    /// SERVI PAR L'AGRÉGATION DU TABLEAU DE BORD, ET C'EST UN CHOIX DISCUTABLE.
    ///
    /// Le profil est un sous-ensemble du tableau de bord. Un handler dédié
    /// n'appellerait qu'un seul service et serait plus léger ; celui-ci en appelle
    /// trois pour n'en garder qu'un.
    ///
    /// Je le laisse tel quel tant que l'écran « Compte » n'est pas maquetté : s'il
    /// affiche la note, les évaluations ou le véhicule — comme la maquette Flutter
    /// le montre — il faudra de toute façon un handler propre. En créer un
    /// aujourd'hui reviendrait à deviner sa forme.
    /// </remarks>
    [HttpGet("profile")]
    [ProducesResponseType<BffEnvelope<DriverProfileDto>>(StatusCodes.Status200OK)]
    public async Task<BffEnvelope<DriverProfileDto>> GetProfileAsync(CancellationToken cancellationToken)
    {
        var dashboard = await _dashboard.HandleAsync(cancellationToken);
        return new BffEnvelope<DriverProfileDto>(dashboard.Data.Driver, dashboard.Warnings);
    }
}
