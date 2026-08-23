using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Merchant;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de HBA Partner — boutiques HBAExpress (§11, §12, §44).</summary>
/// <remarks>
/// `/activities` EST ICI, ET NON DANS UN CONTRÔLEUR NEUTRE.
///
/// Il rend pourtant boutiques ET restaurants. Le §44 en fait le point d'entrée
/// unique du sélecteur d'activité, sous `merchant` : l'application appelle une
/// seule route au démarrage, puis bascule vers le BFF correspondant au type
/// choisi. Le déplacer ailleurs ajouterait une troisième adresse à connaître pour
/// une seule requête.
/// </remarks>
// ═════════════════════════════════════════════════════════════════════════════
// LA POLITIQUE EST POSÉE PAR MÉTHODE, ET NON SUR LA CLASSE. NE PAS REMONTER.
//
// Deux attributs `[Authorize]` — un sur la classe, un sur la méthode — ne se
// remplacent PAS : leurs exigences S'ADDITIONNENT. Un `[Authorize(PartnerOnly)]`
// posé sur `activities` alors que la classe porte `MerchantOnly` n'élargit rien
// du tout ; il exige les DEUX, et le restaurateur reste dehors.
//
// C'est le piège classique de la combinaison de politiques, et il est
// silencieux : le code se lit comme une dérogation, et se comporte comme un
// durcissement. La seule façon d'assouplir une route est donc de retirer la
// politique de la classe et de la porter sur chaque méthode.
//
// Conséquence à tenir : toute méthode AJOUTÉE ici sans `[Authorize]` explicite
// retomberait sur la `FallbackPolicy` de la passerelle — authentifié, sans rôle.
// Un tableau de bord de boutique lisible par n'importe quel inscrit.
// ═════════════════════════════════════════════════════════════════════════════
[ApiController]
[Route("api/v1/bff/merchant")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class MerchantController : ControllerBase
{
    private readonly GetMerchantActivitiesHandler _activities;
    private readonly GetMerchantDashboardHandler _dashboard;

    public MerchantController(
        GetMerchantActivitiesHandler activities, GetMerchantDashboardHandler dashboard)
    {
        _activities = activities;
        _dashboard = dashboard;
    }

    /// <summary>Les activités du compte : boutiques et restaurants.</summary>
    /// <remarks>
    /// AUCUNE DÉPENDANCE N'EST CRITIQUE ICI — cf. le handler.
    ///
    /// C'est le premier écran après la connexion : le faire échouer enferme le
    /// partenaire dehors. Une liste vide avec un avertissement lui laisse au moins
    /// l'application.
    ///
    /// ET C'EST EXACTEMENT CE QUE FAISAIT LA POLITIQUE DU CONTRÔLEUR.
    ///
    /// Le soin apporté ci-dessus à ne jamais faire échouer cette route était
    /// annulé par le `[Authorize(MerchantOnly)]` de la classe : un compte purement
    /// restaurateur (`FoodPartner` sans `Seller`) était refusé AVANT d'atteindre
    /// le handler. Il n'obtenait ni liste vide ni avertissement — un 403, sur la
    /// seule route qui lui aurait appris qu'il a un restaurant.
    ///
    /// `PartnerOnly` accepte les deux rôles. Le tableau de bord d'une boutique,
    /// lui, garde `MerchantOnly` : la politique de classe reste la règle, celle-ci
    /// est l'exception, et elle est portée par la seule méthode concernée.
    /// </remarks>
    [HttpGet("activities")]
    [Authorize(Policy = GatewayPolicies.PartnerOnly)]
    [ProducesResponseType<BffEnvelope<MerchantActivitiesDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<BffEnvelope<MerchantActivitiesDto>> GetActivitiesAsync(
        CancellationToken cancellationToken)
        => _activities.HandleAsync(cancellationToken);

    /// <summary>Tableau de bord d'une boutique.</summary>
    /// <remarks>
    /// 404 si la boutique n'appartient pas au vendeur connecté — la route amont
    /// est scopée par vendeur et le `sellerId` vient du jeton (§30).
    /// </remarks>
    [HttpGet("stores/{storeId:guid}/dashboard")]
    [Authorize(Policy = GatewayPolicies.MerchantOnly)]
    [ProducesResponseType<BffEnvelope<MerchantDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<MerchantDashboardDto>> GetStoreDashboardAsync(
        Guid storeId, CancellationToken cancellationToken)
        => _dashboard.HandleAsync(storeId, cancellationToken);
}
