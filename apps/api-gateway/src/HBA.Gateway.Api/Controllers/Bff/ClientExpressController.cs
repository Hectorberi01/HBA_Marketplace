using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de l'application cliente — univers HBAExpress.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE CONTRÔLEUR NE SERT JAMAIS DE DONNÉES DE RESTAURATION (§6, §45).
///
/// La séparation des deux univers est une exigence produit. Ajouter ici une
/// section « restaurants près de chez vous » ferait apparaître HBA Food dans
/// l'accueil marketplace, et la frontière disparaîtrait d'abord du code, puis de
/// l'écran.
///
/// VERSION DANS L'URL : `/api/v1/bff/...` (§28).
///
/// Une seule stratégie, appliquée partout. L'en-tête `Accept-Version` serait plus
/// élégant, mais invisible dans un journal d'accès, dans une capture réseau et
/// dans un signalement d'utilisateur — trois endroits où l'on cherche justement
/// quelle version a répondu.
///
/// AUCUNE AGRÉGATION ICI (§47). Le contrôleur traduit HTTP ↔ handler.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
    /// <remarks>
    /// ANONYME, ET LES SECTIONS PERSONNELLES SE TAISENT D'ELLES-MÊMES.
    ///
    /// L'accueil doit s'afficher avant toute connexion, sinon un visiteur ne voit
    /// rien. Recommandations et commande en cours viennent de services
    /// authentifiés : sans session ils rendent 401, et l'agrégateur les traite
    /// comme des absences. Aucun filtre conditionnel n'est donc nécessaire ici —
    /// c'est la criticité déclarée qui fait le travail.
    /// </remarks>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<ExpressHomeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<ExpressHomeDto>> GetHomeAsync(CancellationToken cancellationToken)
        => _home.HandleAsync(cancellationToken);

    /// <summary>Fiche produit.</summary>
    /// <remarks>
    /// Anonyme également : une fiche produit doit être partageable par lien. La
    /// note et les avis, servis par un service authentifié, seront simplement
    /// absents pour un visiteur — cf. `IEngagementClient`.
    /// </remarks>
    [HttpGet("products/{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<ProductDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<ProductDetailDto>> GetProductAsync(
        Guid productId, CancellationToken cancellationToken)
        => _productDetail.HandleAsync(productId, cancellationToken);
}
