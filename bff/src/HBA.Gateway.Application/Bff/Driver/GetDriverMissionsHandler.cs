using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>Missions du livreur, et détail d'une mission (§15, §16).</summary>
/// <remarks>
/// LA PAGINATION EST APPLIQUÉE ICI, FAUTE DE L'ÊTRE EN AMONT.
///
/// <c>/drivers/me/missions</c> rend TOUT l'historique, sans filtre ni page. La
/// passerelle tronque donc après coup : le réseau entre services a déjà porté la
/// totalité. C'est correct mais coûteux, et le coût grandit avec l'ancienneté du
/// livreur — c'est-à-dire chez les plus fidèles.
///
/// Manque à combler : <c>?status=&amp;page=&amp;pageSize=</c> côté service.
/// </remarks>
public sealed class GetDriverMissionsHandler
{
    public const string ScreenId = "driver.missions";
    public const string DetailScreenId = "driver.mission_detail";

    private readonly IDeliveryClient _delivery;

    public GetDriverMissionsHandler(IDeliveryClient delivery) => _delivery = delivery;

    public async Task<BffEnvelope<PagedResult<DriverMissionDto>>> ListAsync(
        PageRequest page, bool activeOnly, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var missions = context.Resolve(
            DependencyCriticality.Critical,
            "Delivery",
            await context.CallAsync("Delivery", () => _delivery.ListMyMissionsAsync(cancellationToken)))!;

        var filtered = missions
            .Where(mission => !activeOnly || DriverProjections.IsActive(mission.Status))
            .OrderByDescending(mission => mission.OfferedAtUtc ?? DateTime.MinValue)
            .Select(DriverProjections.ToDto)
            .ToList();

        return context.Complete(PagedResult<DriverMissionDto>.Of(page.Apply(filtered), page));
    }

    /// <summary>
    /// Détail d'une mission.
    /// </summary>
    /// <remarks>
    /// LA MISSION EST CHERCHÉE DANS **SES** MISSIONS, PAS PAR IDENTIFIANT DIRECT.
    ///
    /// <c>GET /api/deliveries/{id}</c> existe et rendrait la course en un appel.
    /// Il n'est PAS utilisé : rien n'y garantit que la course appartient au
    /// livreur qui la demande. Passer par <c>/drivers/me/missions</c>, que le
    /// service filtre sur le jeton, rend l'accès à la course d'un autre
    /// structurellement impossible — plutôt que dépendant d'un contrôle qu'il
    /// faudrait vérifier ailleurs.
    ///
    /// Le prix est un appel plus lourd. C'est le bon prix.
    /// </remarks>
    public async Task<BffEnvelope<DriverMissionDto>> GetAsync(
        Guid deliveryId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(DetailScreenId);

        var missions = context.Resolve(
            DependencyCriticality.Critical,
            "Delivery",
            await context.CallAsync("Delivery", () => _delivery.ListMyMissionsAsync(cancellationToken)))!;

        var mission = missions.FirstOrDefault(m => m.DeliveryId == deliveryId);

        if (mission is null)
        {
            // « Introuvable » et non « interdit » : un 403 confirmerait au livreur
            // que la course existe et appartient à quelqu'un d'autre.
            throw new BffResourceNotFoundException("Mission", deliveryId);
        }

        return context.Complete(DriverProjections.ToDto(mission));
    }
}
