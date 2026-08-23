using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Restaurant;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF de HBA Partner — activités HBA Food (§13, §14).</summary>
/// <remarks>
/// `RestaurantOnly` MAPPE SUR LE RÔLE `FoodPartner`.
///
/// Le nom de la politique est celui du produit, le rôle est celui qu'émet
/// identity-service. La correspondance vit dans `appsettings:Authorization:Roles`,
/// et c'est elle qui empêche d'avoir à renommer un rôle en base pour changer un
/// libellé de politique.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// MANQUE AMONT : PERSONNE N'ATTRIBUE `FoodPartner`.
///
/// Le rôle est bien SEMÉ par identity-service (`IdentityDataSeeder`), mais aucun
/// code ne l'ASSIGNE : ni l'enregistrement d'un restaurant, ni sa validation, ni
/// l'ajout d'un membre du personnel n'appelle `AssignRoleCommand`. Seul « Buyer »
/// est attribué, à l'inscription.
///
/// Conséquence concrète : ces deux routes répondront 403 à TOUT LE MONDE tant
/// que le raccordement n'existe pas — y compris au fondateur du restaurant.
///
/// Et même une fois le fondateur servi, un cuisinier ou un caissier ajouté par le
/// §8 n'obtiendra pas davantage le rôle : l'écran de cuisine, qui est fait POUR
/// eux, leur resterait fermé.
///
/// Ce n'est pas corrigeable ici. Élargir la politique à `Authenticated` rendrait
/// ces routes accessibles à n'importe quel compte, et l'appartenance vérifiée
/// dans les gestionnaires (§30) protège les DONNÉES, pas la SURFACE. Le
/// raccordement doit se faire dans food-service, à la validation du dossier et à
/// l'ajout d'un membre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
[ApiController]
[Route("api/v1/bff/restaurant")]
[Authorize(Policy = GatewayPolicies.RestaurantOnly)]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class RestaurantController : ControllerBase
{
    private readonly GetRestaurantDashboardHandler _dashboard;
    private readonly GetRestaurantKitchenHandler _kitchen;

    public RestaurantController(
        GetRestaurantDashboardHandler dashboard, GetRestaurantKitchenHandler kitchen)
    {
        _dashboard = dashboard;
        _kitchen = kitchen;
    }

    /// <summary>Tableau de bord du restaurant.</summary>
    /// <remarks>
    /// L'identifiant de l'URL est VÉRIFIÉ contre celui résolu depuis le jeton :
    /// s'il diffère, 404. Il ne sert jamais à choisir ce qu'on agrège.
    /// </remarks>
    [HttpGet("restaurants/{restaurantId:guid}/dashboard")]
    [ProducesResponseType<BffEnvelope<RestaurantDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<BffEnvelope<RestaurantDashboardDto>> GetDashboardAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => _dashboard.HandleAsync(restaurantId, cancellationToken);

    /// <summary>Écran de cuisine (KDS).</summary>
    /// <remarks>
    /// NI PORTEFEUILLE, NI COMMISSION, NI REVENU (§14).
    ///
    /// Cet écran tourne sur une tablette posée en cuisine, allumée toute la
    /// journée, que voient les cuisiniers, les extras et parfois les livreurs.
    /// Le type de réponse ne porte AUCUN montant — pas même le total de la
    /// commande.
    /// </remarks>
    [HttpGet("restaurants/{restaurantId:guid}/kitchen")]
    [ProducesResponseType<BffEnvelope<RestaurantKitchenDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<BffEnvelope<RestaurantKitchenDto>> GetKitchenAsync(
        Guid restaurantId, CancellationToken cancellationToken)
        => _kitchen.HandleAsync(restaurantId, cancellationToken);
}
