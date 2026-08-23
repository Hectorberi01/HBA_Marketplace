using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Client;

/// <summary>Façade BFF de l'application cliente — univers HBAExpress.</summary>
/// <remarks>
/// CE CONTRÔLEUR NE DOIT JAMAIS SERVIR DE DONNÉES DE RESTAURATION.
///
/// La séparation des deux univers est une exigence produit (§13) : l'application
/// affiche deux expériences distinctes. Ajouter ici une section « restaurants
/// près de chez vous » ferait apparaître HBA Food dans l'accueil marketplace, et
/// la frontière disparaîtrait d'abord du code, puis de l'écran.
/// </remarks>
[ApiController]
// ROUTE HÉRITÉE, CONSERVÉE LE TEMPS DE LA BASCULE DES CLIENTS.
//
// La version canonique est `/api/v1/bff/client/express/home`, servie par
// `ClientExpressController`. Celle-ci reste branchée sur le mécanisme CONFIGURÉ
// — sections `JsonElement` — et disparaîtra quand les trois applications auront
// basculé. La supprimer aujourd'hui casserait des binaires déjà installés.
[Obsolete("Utiliser /api/v1/bff/client/express/home. Retrait prévu après bascule des clients.")]
[Route("api/bff/client/express")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class ExpressBffController : ControllerBase
{
    private readonly ExpressHomeService _home;

    public ExpressBffController(ExpressHomeService home) => _home = home;

    /// <summary>
    /// Accueil marketplace, agrégé à partir des sections configurées.
    /// </summary>
    /// <remarks>
    /// Anonyme : l'accueil doit s'afficher avant toute connexion, sinon
    /// l'application ne peut rien montrer à un visiteur. Les sections qui
    /// exigeraient une identité — « vos commandes en cours » — devront être
    /// servies par un point de terminaison distinct et authentifié, et non
    /// ajoutées ici avec un filtre conditionnel.
    /// </remarks>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffHomeResponse>(StatusCodes.Status200OK)]
    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _home.GetHomeAsync(cancellationToken);
}
