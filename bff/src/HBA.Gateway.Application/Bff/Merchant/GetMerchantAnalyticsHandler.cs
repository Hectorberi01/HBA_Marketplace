using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>L'écran de courbes du vendeur, sur une période choisie.</summary>
/// <param name="Sales">La série. `null` si analytics est muet ou refuse.</param>
public sealed record MerchantAnalyticsDto(MerchantSalesSeriesDto? Sales);

/// <summary>
/// Les ventes du vendeur connecté, sur la période demandée.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CET ÉCRAN EXISTE À CÔTÉ DU TABLEAU DE BORD.
///
/// Le tableau de bord porte déjà trente jours, et c'est délibérément figé : il
/// s'ouvre d'un coup et ne se règle pas. Ici la période est un paramètre, et
/// c'est la seule différence — assez pour justifier une route, pas assez pour
/// justifier une seconde traduction du contrat. `Vers` est partagée avec le
/// tableau de bord.
///
/// LE VENDEUR VIENT DU JETON, PAS DE L'URL, ET C'EST LE §30.
///
/// `GetMySellerAsync` résout le dossier du compte connecté. La route publique
/// n'a donc AUCUN segment de vendeur à falsifier. C'est aussi ce qui distingue
/// cette route de `GET /api/sellers/{id}/analytics/sales`, relayée telle quelle
/// par `ReverseProxy` : celle-là est adressée par l'URL et gardée en face par
/// l'appartenance ; celle-ci ne peut désigner que soi.
///
/// CRITICITÉ (§23)
///   Merchant   CRITIQUE   — sans sellerId, il n'y a rien à demander.
///   Analytics  IMPORTANTE — la courbe manque, l'écran affiche son absence.
///
/// Analytics reste IMPORTANTE ALORS QU'ELLE EST LE SUJET DE L'ÉCRAN, et c'est
/// un choix : un membre d'équipe sans `SELLER_ANALYTICS_VIEW` reçoit 403, ce qui
/// est une réponse LÉGITIME et non une panne. En critique, il verrait une page
/// d'erreur là où il doit voir « vous n'avez pas accès à ces chiffres ».
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetMerchantAnalyticsHandler
{
    public const string ScreenId = "merchant.analytics";

    /// <summary>Profondeur par défaut, en jours.</summary>
    public const int DefaultDays = 30;

    /// <summary>
    /// Profondeur maximale, en jours.
    /// </summary>
    /// <remarks>
    /// LA MÊME BORNE QUE LE SERVICE, ET RECOPIÉE ICI EN CONNAISSANCE DE CAUSE.
    ///
    /// analytics-service refuse au-delà de 366 avec une erreur de validation.
    /// Borner ici aussi évite un aller-retour pour un refus prévisible, et rend
    /// la troncature explicite plutôt que surprenante. Le prix est une constante
    /// en double : si le service desserre sa borne, celle-ci devient la vraie
    /// limite, sans que rien ne le signale. C'est assumé — l'inverse, laisser
    /// passer, ferait porter au vendeur un 400 qu'il ne peut pas comprendre.
    /// </remarks>
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
        //
        // Il est borné ici, puis converti en deux `DateOnly`. Le client typé
        // n'accepte que des dates : aucun segment de chemin ne peut venir de la
        // requête entrante — c'est la garde écrite dans `IServiceClient`.
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
