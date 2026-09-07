using HBA.Gateway.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers;

/// <summary>Politique de version d'une application mobile.</summary>
/// <param name="MinSupportedBuild">
/// En dessous, l'application se bloque sur l'écran « mise à jour requise ».
/// </param>
/// <param name="LatestBuild">Dernier build publié. Sert à proposer, pas à bloquer.</param>
/// <param name="Message">Texte affiché au blocage.</param>
public sealed record AppVersionPolicy(
    int MinSupportedBuild,
    int LatestBuild,
    string? UpdateUrlAndroid,
    string? UpdateUrlIos,
    string? Message);

/// <summary>Politique de version, par application.</summary>
public sealed class AppVersionOptions
{
    public const string SectionName = "AppVersions";

    /// <summary>Clé = identifiant d'application (« seller », « client », « driver »).</summary>
    public Dictionary<string, AppVersionPolicy> Apps { get; init; } = new();
}

/// <summary>Le minimum de version supporté, par application.</summary>
[ApiController]
[Route("api/app")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class AppVersionController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppVersionController(IConfiguration configuration) => _configuration = configuration;

    /// <summary>La politique de version de <paramref name="app"/>.</summary>
    [HttpGet("{app}/version")]
    [ProducesResponseType(typeof(AppVersionPolicy), StatusCodes.Status200OK)]
    public ActionResult<AppVersionPolicy> Version(string app)
    {
        Response.Headers.CacheControl = "public, max-age=300";

        var section = _configuration.GetSection($"{AppVersionOptions.SectionName}:{app}");

        // `Get<T>()` REND `null` SI LA SECTION N'EXISTE PAS, pas une instance vide.
        var politique = section.Get<AppVersionPolicy>()
            ?? new AppVersionPolicy(0, 0, null, null, null);

        return Ok(politique);
    }
}
