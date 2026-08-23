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
/// <param name="Message">
/// Texte affiché au blocage. Facultatif — l'application a le sien par défaut.
/// </param>
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
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS DU MÉTIER, C'EST DE LA CONFIGURATION — ET ÇA CHANGE TOUT.
///
/// Aucun domaine, aucune table, aucune migration : la politique de version est
/// une décision d'exploitation, qu'on ajuste le jour d'une livraison. La loger
/// dans un des treize services lui donnerait une base de données et un cycle de
/// déploiement dont elle n'a aucun besoin — et ferait dépendre le démarrage de
/// TOUTES les applications de la santé de ce service-là.
///
/// Elle vit donc sur la passerelle, comme le faisait le BFF du monolithe
/// (`SellerAppEndpoints.GetVersionPolicy`, qui lisait déjà `IConfiguration`).
///
/// ANONYME, ET C'EST OBLIGATOIRE. La porte de version se franchit AVANT la
/// connexion : c'est le tout premier appel de l'application, sur l'écran de
/// démarrage. L'exiger authentifiée rendrait impossible le blocage d'une version
/// dont le parcours de connexion est justement cassé — le cas où l'on en a le
/// plus besoin.
///
/// 200 AVEC UNE POLITIQUE PERMISSIVE PLUTÔT QUE 404 SUR UNE APP INCONNUE.
///
/// Un 404 obligerait chaque application à distinguer « je ne suis pas
/// configurée » de « le serveur est en panne ». La réponse par défaut
/// (`MinSupportedBuild = 0`) ne bloque personne et dit exactement cela : aucune
/// politique n'est en vigueur pour cette application.
///
/// CACHE COURT — CINQ MINUTES, PAS UNE JOURNÉE.
///
/// Contrairement au référentiel géographique, cette valeur est faite pour
/// changer vite : on relève le minimum le jour où l'on retire une version
/// défaillante. Un cache d'une journée retarderait d'autant le blocage qu'on
/// vient de décider.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

        // `Get<T>()` REND `null` SI LA SECTION N'EXISTE PAS, pas une instance
        // vide. Sans le repli, une application non configurée recevrait `null`
        // sérialisé — et son analyseur JSON échouerait au tout premier appel,
        // sur l'écran de démarrage.
        var politique = section.Get<AppVersionPolicy>()
            ?? new AppVersionPolicy(0, 0, null, null, null);

        return Ok(politique);
    }
}
