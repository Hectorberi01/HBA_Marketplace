using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Gateway.Application.Bff.Admin;
using HBA.Gateway.Application.Bff.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Gateway.Api.Controllers.Bff;

/// <summary>Façade BFF du back-office.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA POLITIQUE `Admin` EST POSÉE SUR LA CLASSE, PAS SUR CHAQUE ACTION.
///
/// Un `[Authorize]` par action est un `[Authorize]` qu'on oublie — et l'action
/// oubliée est servie à tout le monde sans que rien ne le signale : elle compile,
/// elle répond, elle passe les tests fonctionnels. Sur une façade
/// d'ADMINISTRATION, cet oubli-là expose des files de modération et, demain, des
/// versements.
///
/// Posée sur la classe, la politique couvre par défaut tout ce qu'on ajoutera
/// ici. Une action délibérément publique devra écrire `[AllowAnonymous]` —
/// c'est-à-dire un geste explicite, visible en revue, qu'on ne fait pas par
/// distraction.
///
/// CE CONTRÔLEUR N'EST PAS UN RELAIS.
///
/// Les cent et quelques points d'entrée d'administration des services sont
/// atteints par `ReverseProxy` — l'application admin les appelle directement. Ne
/// vivent ici que les écrans qu'AUCUN service ne peut rendre seul, parce qu'ils
/// croisent plusieurs services. Y ajouter un relais déguisé ferait de la
/// passerelle un second endroit où l'autorisation d'un geste métier se décide.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
[ApiController]
[Route("api/v1/bff/admin")]
[Authorize(Policy = GatewayPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitingExtensions.ReadPolicy)]
public sealed class AdminController : ControllerBase
{
    private readonly GetAdminQueuesHandler _queues;

    public AdminController(GetAdminQueuesHandler queues) => _queues = queues;

    /// <summary>Les files d'attente d'administration, en un seul appel.</summary>
    /// <remarks>
    /// AUCUNE FILE N'EST CRITIQUE : UN SERVICE À TERRE NE COÛTE PAS L'ÉCRAN.
    ///
    /// Les quatre autres files restent lisibles et l'administrateur travaille ;
    /// celle qui manque vaut `null` et porte son avertissement. C'est pourquoi
    /// cette action ne déclare pas 503 — contrairement aux façades client, elle
    /// n'a aucune dépendance dont l'absence viderait l'écran de son sens.
    /// </remarks>
    [HttpGet("queues")]
    [ProducesResponseType<BffEnvelope<AdminQueuesDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<BffEnvelope<AdminQueuesDto>> GetQueuesAsync(CancellationToken cancellationToken)
        => _queues.HandleAsync(cancellationToken);
}
