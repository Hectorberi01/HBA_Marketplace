using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Application.Bff.Client.Food;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de l'application cliente — univers HBA Food.</summary>
/// <remarks>
/// CE CONTRÔLEUR NE SERT JAMAIS DE PRODUIT MARKETPLACE (§8, §45).
///
/// Symétrique exact de <c>ClientExpressController</c>. Ajouter ici une section
/// « produits recommandés » ferait entrer HBAExpress dans l'accueil restauration,
/// et la frontière disparaîtrait d'abord du code, puis de l'écran.
/// </remarks>
[ApiController]
[Route("api/v1/bff/client/food")]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class ClientFoodController : ControllerBase
{
    private readonly GetFoodHomeHandler _home;
    private readonly GetRestaurantDetailHandler _detail;

    public ClientFoodController(GetFoodHomeHandler home, GetRestaurantDetailHandler detail)
    {
        _home = home;
        _detail = detail;
    }

    /// <summary>Vitrine des restaurants.</summary>
    /// <remarks>
    /// Anonyme : une vitrine qu'on ne peut pas parcourir sans compte ne convertit
    /// personne. La commande en cours, servie par un service authentifié, sera
    /// simplement absente pour un visiteur.
    /// </remarks>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<FoodHomeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<FoodHomeDto>> GetHomeAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        // Les bornes sont appliquées par `PageRequest`, jamais laissées au
        // service amont : `pageSize=100000` doit être ramené AVANT de partir.
        => _home.HandleAsync(new PageRequest(page, pageSize), cancellationToken);

    /// <summary>Fiche d'un restaurant, carte comprise.</summary>
    /// <remarks>
    /// 404 pour un établissement hors vitrine, sans le distinguer d'un
    /// identifiant inexistant — le service amont applique déjà cette règle.
    /// </remarks>
    [HttpGet("restaurants/{restaurantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<BffEnvelope<FoodRestaurantDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<FoodRestaurantDetailDto>> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => _detail.HandleAsync(restaurantId, cancellationToken);
}
