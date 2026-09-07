using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>L'écran de courbes du vendeur, sur une période choisie.</summary>
/// <param name="Sales">La série. `null` si analytics est muet ou refuse.</param>
public sealed record MerchantAnalyticsDto(MerchantSalesSeriesDto? Sales);

/// <summary>Les ventes du vendeur connecté, sur la période demandée.</summary>
public sealed class GetMerchantAnalyticsHandler
{
    public const string ScreenId = "merchant.analytics";

    /// <summary>Profondeur par défaut, en jours.</summary>
    public const int DefaultDays = 30;

    /// <summary>Profondeur maximale, en jours.</summary>
    public const int MaxDays = 366;

    private readonly IMerchantClient _merchant;
    private readonly IAnalyticsClient _analytics;

    public GetMerchantAnalyticsHandler(IMerchantClient merchant, IAnalyticsClient analytics)
    {
        _merchant = merchant;
        _analytics = analytics;
    }

    public async Task<BffEnvelope<MerchantAnalyticsDto>> HandleAsync(
        int? days, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var seller = context.Resolve(
            DependencyCriticality.Critical,
            "Merchant",
            await context.CallAsync("Merchant", () => _merchant.GetMySellerAsync(cancellationToken)))!;

        // `days` VIENT DU CLIENT, ET IL NE TOUCHE JAMAIS LE CHEMIN.
        var profondeur = Math.Clamp(days ?? DefaultDays, 1, MaxDays);

        var aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);
        var debut = aujourdhui.AddDays(-(profondeur - 1));

        var serie = context.Resolve(
            DependencyCriticality.Important,
            "Analytics",
            await context.CallAsync("Analytics", () => _analytics.GetSellerSalesAsync(
                seller.Id, debut, aujourdhui, cancellationToken)));

        return context.Complete(new MerchantAnalyticsDto(
            serie is null ? null : GetMerchantDashboardHandler.Vers(serie)));
    }
}
