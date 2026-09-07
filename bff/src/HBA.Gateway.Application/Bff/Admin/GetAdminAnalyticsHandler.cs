using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Admin;

/// <summary>Un jour d'activité de la plateforme.</summary>
/// <param name="Day">La journée, en UTC.</param>
/// <param name="OrdersCount">Commandes confirmées, toutes natures.</param>
/// <param name="Gmv">Volume marchand — voir l'encadré d'<see cref="AdminAnalyticsDto"/>.</param>
/// <param name="GoodsOrdersCount">Dont marchandise.</param>
/// <param name="FoodOrdersCount">Dont repas.</param>
public sealed record AdminActivityPointDto(
    DateOnly Day, int OrdersCount, decimal Gmv, int GoodsOrdersCount, int FoodOrdersCount);

/// <summary>Un jour d'inscriptions.</summary>
/// <param name="Day">La journée, en UTC.</param>
/// <param name="Buyers">Comptes créés. INCLUT les futurs vendeurs.</param>
/// <param name="Sellers">Dossiers vendeur ouverts.</param>
public sealed record AdminSignupPointDto(DateOnly Day, int Buyers, int Sellers);

/// <summary>Les courbes du back-office, en un seul appel.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Currency">Devise des montants. `null` si l'activité est indisponible.</param>
/// <param name="Activity">Commandes et volume, jour par jour.</param>
/// <param name="Signups">Inscriptions, jour par jour.</param>
/// <param name="TotalOrders">
/// Commandes de la période. `null` si l'activité est indisponible.
/// </param>
/// <param name="TotalGmv">Volume marchand de la période.</param>
public sealed record AdminAnalyticsDto(
    DateOnly From,
    DateOnly To,
    string? Currency,
    IReadOnlyList<AdminActivityPointDto>? Activity,
    IReadOnlyList<AdminSignupPointDto>? Signups,
    int? TotalOrders,
    decimal? TotalGmv);

/// <summary>Les deux courbes du back-office, en un seul appel.</summary>
public sealed class GetAdminAnalyticsHandler
{
    public const string ScreenId = "admin.analytics";

    /// <summary>Profondeur par défaut, en jours.</summary>
    public const int DefaultDays = 30;

    /// <summary>Profondeur maximale, en jours — la borne d'analytics-service.</summary>
    public const int MaxDays = 366;

    private readonly IAnalyticsClient _analytics;

    public GetAdminAnalyticsHandler(IAnalyticsClient analytics) => _analytics = analytics;

    public async Task<BffEnvelope<AdminAnalyticsDto>> HandleAsync(
        int? days, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var profondeur = Math.Clamp(days ?? DefaultDays, 1, MaxDays);
        var aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);
        var debut = aujourdhui.AddDays(-(profondeur - 1));

        var activiteTask = context.CallAsync(
            "Analytics", () => _analytics.GetPlatformActivityAsync(debut, aujourdhui, cancellationToken));

        var inscriptionsTask = context.CallAsync(
            "Analytics", () => _analytics.GetSignupsAsync(debut, aujourdhui, cancellationToken));

        await Task.WhenAll(activiteTask, inscriptionsTask);

        var activite = context.Resolve(
            DependencyCriticality.Important, "Analytics", await activiteTask);

        var inscriptions = context.Resolve(
            DependencyCriticality.Important, "Analytics", await inscriptionsTask);

        return context.Complete(new AdminAnalyticsDto(
            From: debut,
            To: aujourdhui,
            Currency: activite?.Currency,
            Activity: activite is null
                ? null
                : [.. activite.Points.Select(point => new AdminActivityPointDto(
                    point.Day, point.OrdersCount, point.Gmv,
                    point.GoodsOrdersCount, point.FoodOrdersCount))],
            Signups: inscriptions is null
                ? null
                : [.. inscriptions.Points.Select(point => new AdminSignupPointDto(
                    point.Day, point.Buyers, point.Sellers))],
            TotalOrders: activite?.TotalOrders,
            TotalGmv: activite?.TotalGmv));
    }
}
