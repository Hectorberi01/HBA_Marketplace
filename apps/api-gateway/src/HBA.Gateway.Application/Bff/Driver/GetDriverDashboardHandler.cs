using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>
/// Tableau de bord du livreur (§15).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CRITICITÉ (§23)
///
///   Delivery · compte    CRITIQUE   — sans compte livreur, il n'y a pas d'écran.
///   Delivery · missions  IMPORTANTE — l'accueil s'affiche sans mission en cours.
///   Financial            IMPORTANTE — les gains manquent, l'écran reste utile.
///
/// TROIS VAGUES, ET LES DEUX PREMIÈRES SONT IMPOSÉES PAR LES DONNÉES.
///
/// Le portefeuille exige un <c>driverId</c> que seul le compte peut donner :
/// impossible de le lancer avant. Les missions, elles, ne dépendent que du jeton
/// et partent DÈS LA PREMIÈRE VAGUE, en parallèle du compte — c'est ce qui évite
/// de payer trois allers-retours en série.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetDriverDashboardHandler
{
    public const string ScreenId = "driver.dashboard";

    private readonly IDeliveryClient _delivery;
    private readonly IFinancialClient _financial;

    public GetDriverDashboardHandler(IDeliveryClient delivery, IFinancialClient financial)
    {
        _delivery = delivery;
        _financial = financial;
    }

    public async Task<BffEnvelope<DriverDashboardDto>> HandleAsync(CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        // ── Vague 1 : compte et missions, sans dépendance entre eux ──────────
        var accountTask = context.CallAsync(
            "Delivery", () => _delivery.GetMyDriverAccountAsync(cancellationToken));

        var missionsTask = context.CallAsync(
            "Delivery", () => _delivery.ListMyMissionsAsync(cancellationToken));

        await Task.WhenAll(accountTask, missionsTask);

        var account = context.Resolve(
            DependencyCriticality.Critical, "Delivery", await accountTask)!;

        var missions = context.Resolve(
            DependencyCriticality.Important, "Delivery", await missionsTask);

        // ── Vague 2 : le portefeuille, qui exige le driverId de la vague 1 ───
        var wallet = context.Resolve(
            DependencyCriticality.Important,
            "Financial",
            await context.CallAsync(
                "Financial",
                () => _financial.GetDriverWalletAsync(account.DriverId, cancellationToken)));

        var current = missions?
            .Where(mission => DriverProjections.IsActive(mission.Status))
            .OrderByDescending(mission => mission.OfferedAtUtc ?? DateTime.MinValue)
            .Select(DriverProjections.ToDto)
            .FirstOrDefault();

        var dto = new DriverDashboardDto(
            Driver: DriverProjections.ToDto(account),
            Status: account.Availability,
            CurrentMission: current,
            Today: new DriverTodayDto(
                // Cumul de vie, et le nom du champ le dit — cf. `DriverTodayDto`.
                LifetimeDeliveries: account.CompletedDeliveries,
                AvailableBalance: wallet?.AvailableBalance,
                LifetimeEarned: wallet?.LifetimeEarned,
                Currency: wallet?.Currency));

        return context.Complete(dto);
    }
}
