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
/// <param name="Buyers">Comptes créés. INCLUT les futurs vendeurs ET livreurs.</param>
/// <param name="Sellers">Dossiers vendeur ouverts.</param>
/// <param name="Drivers">Comptes livreur ouverts.</param>
public sealed record AdminSignupPointDto(DateOnly Day, int Buyers, int Sellers, int Drivers);

/// <summary>Un jour de tentatives de paiement.</summary>
/// <param name="Day">La journée, en UTC.</param>
/// <param name="Captured">Tentatives encaissées.</param>
/// <param name="Failed">Tentatives échouées.</param>
/// <param name="CapturedAmount">Volume encaissé — PAS le `Gmv`, voir l'encadré.</param>
public sealed record AdminPaymentPointDto(
    DateOnly Day, int Captured, int Failed, decimal CapturedAmount);

/// <summary>Ce qu'un prestataire de paiement a traité sur la période.</summary>
/// <param name="Provider">Le prestataire. « inconnu » pour les messages d'avant le lot 2.</param>
/// <param name="Captured">Tentatives encaissées.</param>
/// <param name="Failed">Tentatives échouées.</param>
/// <param name="CapturedAmount">Volume encaissé.</param>
/// <param name="FailureRate">Taux d'échec du prestataire. `null` sans tentative.</param>
public sealed record AdminPaymentProviderDto(
    string Provider, int Captured, int Failed, decimal CapturedAmount, decimal? FailureRate);

/// <summary>Les courbes du back-office, en un seul appel.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Currency">Devise des montants. `null` si les deux séries chiffrées manquent.</param>
/// <param name="Activity">Commandes et volume, jour par jour.</param>
/// <param name="Signups">Inscriptions, jour par jour.</param>
/// <param name="Payments">Tentatives de paiement, jour par jour.</param>
/// <param name="PaymentProviders">Classement des prestataires sur la période.</param>
/// <param name="TotalOrders">Commandes de la période. `null` si l'activité est indisponible.</param>
/// <param name="TotalGmv">Volume marchand de la période.</param>
/// <param name="TotalCaptured">Tentatives encaissées sur la période.</param>
/// <param name="TotalFailed">Tentatives échouées sur la période.</param>
/// <param name="PaymentFailureRate">Taux d'échec global. `null` sans aucune tentative.</param>
/// <remarks>
/// `Gmv` et `CapturedAmount` ne se comparent pas et ne s'additionnent pas. Le
/// premier est un VOLUME MARCHAND : il ne porte ni frais de livraison ni
/// commission, et vaut zéro pour une commande de repas. Le second est ce que
/// l'acheteur a REELLEMENT paye, frais compris.
///
/// Le taux d'echec mesure « echecs sur issues declarees », pas « echecs sur
/// tentatives » : une intention qui n'aboutit ni a une capture ni a un echec —
/// abandon sur la page du prestataire, webhook jamais recu — n'entre dans aucun
/// terme. Les deux definitions divergent exactement quand un prestataire cesse
/// de repondre, c'est-a-dire au moment ou l'on regarde ce graphe.
/// </remarks>
public sealed record AdminAnalyticsDto(
    DateOnly From,
    DateOnly To,
    string? Currency,
    IReadOnlyList<AdminActivityPointDto>? Activity,
    IReadOnlyList<AdminSignupPointDto>? Signups,
    IReadOnlyList<AdminPaymentPointDto>? Payments,
    IReadOnlyList<AdminPaymentProviderDto>? PaymentProviders,
    int? TotalOrders,
    decimal? TotalGmv,
    int? TotalCaptured,
    int? TotalFailed,
    decimal? PaymentFailureRate);

/// <summary>Les trois courbes du back-office, en un seul appel.</summary>
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

        // LES TROIS COURBES SONT BORNEES SUR LA MEME PERIODE. C'est toute la
        // raison d'etre de cet ecran : trois appels que le client devrait sinon
        // accorder lui-meme, et qu'il finirait par ne plus accorder pareil.
        var paiementsTask = context.CallAsync(
            "Analytics", () => _analytics.GetPaymentsAsync(debut, aujourdhui, cancellationToken));

        await Task.WhenAll(activiteTask, inscriptionsTask, paiementsTask);

        var activite = context.Resolve(
            DependencyCriticality.Important, "Analytics", await activiteTask);

        var inscriptions = context.Resolve(
            DependencyCriticality.Important, "Analytics", await inscriptionsTask);

        var paiements = context.Resolve(
            DependencyCriticality.Important, "Analytics", await paiementsTask);

        return context.Complete(new AdminAnalyticsDto(
            From: debut,
            To: aujourdhui,

            // Les deux series chiffrees resolvent la meme devise par defaut cote
            // service. On prend celle qui a repondu plutot que de rendre `null`
            // parce que l'autre est tombee.
            Currency: activite?.Currency ?? paiements?.Currency,
            Activity: activite is null
                ? null
                : [.. activite.Points.Select(point => new AdminActivityPointDto(
                    point.Day, point.OrdersCount, point.Gmv,
                    point.GoodsOrdersCount, point.FoodOrdersCount))],
            Signups: inscriptions is null
                ? null
                : [.. inscriptions.Points.Select(point => new AdminSignupPointDto(
                    point.Day, point.Buyers, point.Sellers, point.Drivers))],
            Payments: paiements is null
                ? null
                : [.. paiements.Points.Select(point => new AdminPaymentPointDto(
                    point.Day, point.Captured, point.Failed, point.CapturedAmount))],

            // L'ORDRE DU SERVICE EST CONSERVE : du plus gros volume encaisse au
            // plus petit, et non par taux d'echec. Un prestataire a une seule
            // tentative echouee afficherait 100 % et tronerait en tete d'un
            // classement qui ne decide de rien.
            PaymentProviders: paiements is null
                ? null
                : [.. paiements.Providers.Select(p => new AdminPaymentProviderDto(
                    p.Provider, p.Captured, p.Failed, p.CapturedAmount, p.FailureRate))],
            TotalOrders: activite?.TotalOrders,
            TotalGmv: activite?.TotalGmv,
            TotalCaptured: paiements?.TotalCaptured,
            TotalFailed: paiements?.TotalFailed,
            PaymentFailureRate: paiements?.FailureRate));
    }
}
