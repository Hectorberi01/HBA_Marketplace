using HBA.Gateway.Application.Contracts.Analytics;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>analytics-service</c>.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUINZIÈME CLIENT TYPÉ. IL LIT DES ROLL-UPS, PAS DES COMMANDES.
///
/// CE QU'IL REMPLACE, ET CE QU'IL NE REMPLACE PAS.
///
/// Le tableau de bord vendeur comptait « les commandes du jour » en demandant sa
/// liste de commandes à order-service et en filtrant sur la date. Ces trois
/// méthodes-ci rendent le chiffre déjà agrégé.
///
/// Mais elles ne rendent PAS l'état vivant : « commandes à traiter » est un
/// compte de statuts à l'instant présent, et les « dernières commandes » sont des
/// lignes, pas des totaux. Ni l'un ni l'autre n'est un roll-up journalier —
/// l'appel à order-service reste donc nécessaire, et le prétendre remplacé
/// serait faux.
///
/// LES BORNES SONT DES `DateOnly`, ET C'EST UNE GARDE, PAS UN CONFORT.
///
/// Le contrat d'`IServiceClient` interdit qu'un segment de chemin vienne de la
/// requête entrante : ce serait un proxy ouvert vers le réseau interne. Une date
/// typée ne peut pas porter de chemin — elle est analysée par le contrôleur, ou
/// dérivée d'un nombre de jours borné, avant d'arriver ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IAnalyticsClient : IServiceClient
{
    /// <summary>
    /// <c>GET /api/sellers/{sellerId}/analytics/sales</c> — AUTHENTIFIÉ, et gardé
    /// en face par l'appartenance vendeur plus <c>SELLER_ANALYTICS_VIEW</c>.
    /// </summary>
    /// <remarks>
    /// UN 403 ICI N'EST PAS UNE PANNE. Un membre d'équipe sans la capacité reçoit
    /// 403 ; la section doit alors manquer, pas l'écran. C'est le rôle de
    /// `DependencyCriticality.Important` chez l'appelant.
    /// </remarks>
    Task<ServiceResult<SellerSalesSeries>> GetSellerSalesAsync(
        Guid sellerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary><c>GET /api/admin/analytics/activity</c> — RÔLE ADMIN exigé en face.</summary>
    Task<ServiceResult<PlatformActivitySeries>> GetPlatformActivityAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary><c>GET /api/admin/analytics/signups</c> — RÔLE ADMIN exigé en face.</summary>
    Task<ServiceResult<SignupSeries>> GetSignupsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
