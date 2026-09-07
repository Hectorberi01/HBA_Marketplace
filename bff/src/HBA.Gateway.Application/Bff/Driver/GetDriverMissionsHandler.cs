using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>Missions du livreur, et détail d'une mission (§15, §16).</summary>
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

    /// <summary>Détail d'une mission.</summary>
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
