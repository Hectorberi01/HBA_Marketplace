using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Analytics;
using HBA.Gateway.Application.Contracts.Order;

namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>
/// Tableau de bord d'une boutique (§12).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'APPARTENANCE EST VÉRIFIÉE PAR CONSTRUCTION, PAS PAR UN CONTRÔLE (§30).
///
/// La route amont est <c>GET /api/merchants/{sellerId}/stores/{storeId}</c>, et
/// le <c>sellerId</c> employé ici vient de <c>GET /api/merchants/me</c> — donc du
/// jeton. Un vendeur qui demande la boutique d'un autre interroge donc
/// « SES boutiques, identifiant X » : le service ne la trouve pas et rend 404.
///
/// Être `Seller` ne suffit pas à ouvrir la boutique Y — l'exigence exacte du §30 —
/// et cette garantie ne repose sur aucun `if` qu'on pourrait oublier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CHIFFRES DU JOUR NE SONT PLUS CALCULÉS ICI.
///
/// Ils viennent d'analytics-service, qui tient un roll-up journalier. Ce que ça
/// change de sens — commandes CONFIRMÉES et non PLACÉES — est écrit dans
/// `MerchantTodayDto`, et il faut l'avoir lu avant de comparer à un écran d'hier.
///
/// L'APPEL À order-service N'A PAS DISPARU POUR AUTANT, et le prétendre serait
/// faux. Il reste nécessaire pour deux choses qu'aucun roll-up journalier ne
/// peut rendre :
///
///   • `OrdersToProcess` — un compte de STATUTS à l'instant présent ;
///   • `RecentOrders`    — des lignes, pas des totaux.
///
/// Ce qui a été gagné n'est donc pas un appel en moins : c'est l'EXACTITUDE. La
/// liste amont est bornée à cinquante commandes, et les chiffres du jour étaient
/// calculés sur cet échantillon sans que rien ne le signale.
///
/// CRITICITÉ (§23)
///   Merchant · vendeur   CRITIQUE   — sans sellerId, rien n'est interrogeable.
///   Merchant · boutique  CRITIQUE   — c'est le sujet de l'écran ; 404 ⇒ 404.
///   Analytics            IMPORTANTE — les chiffres et la courbe manquent, l'écran vit.
///   Order                IMPORTANTE — les dernières commandes manquent, l'écran vit.
///   Financial            IMPORTANTE — le solde manque, l'écran vit.
///
/// Analytics est IMPORTANTE et non CRITIQUE aussi parce qu'un 403 y est une
/// réponse LÉGITIME : un membre d'équipe sans `SELLER_ANALYTICS_VIEW` doit voir
/// son écran sans les courbes, pas une page d'erreur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetMerchantDashboardHandler
{
    public const string ScreenId = "merchant.store_dashboard";

    /// <summary>Commandes récentes affichées.</summary>
    public const int RecentOrderCount = 5;

    /// <summary>
    /// Profondeur de la courbe du tableau de bord, en jours.
    /// </summary>
    /// <remarks>
    /// TRENTE, ET LA BORNE DU SERVICE EST À 366.
    ///
    /// C'est l'écran d'accueil, pas l'écran d'analyse : trente points se lisent
    /// sur un téléphone, trois cent soixante-six s'y écrasent. La période longue
    /// se demande sur `GET /api/v1/bff/merchant/analytics`, qui existe pour ça.
    /// </remarks>
    public const int SalesWindowDays = 30;

    /// <summary>
    /// Statuts d'une commande qui attend une action du vendeur.
    /// </summary>
    /// <remarks>
    /// RELEVÉS DANS LE DOMAINE, À CONFIRMER — order-service n'expose aucun
    /// filtre. Un statut manquant fait disparaître des commandes du compteur
    /// « à traiter », sans erreur : le vendeur croit n'avoir rien à faire.
    /// </remarks>
    private static readonly string[] ToProcess = ["Paid", "Confirmed", "Preparing"];

    private readonly IMerchantClient _merchant;
    private readonly IOrderClient _order;
    private readonly IFinancialClient _financial;
    private readonly IAnalyticsClient _analytics;

    public GetMerchantDashboardHandler(
        IMerchantClient merchant,
        IOrderClient order,
        IFinancialClient financial,
        IAnalyticsClient analytics)
    {
        _merchant = merchant;
        _order = order;
        _financial = financial;
        _analytics = analytics;
    }

    public async Task<BffEnvelope<MerchantDashboardDto>> HandleAsync(
        Guid storeId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var seller = context.Resolve(
            DependencyCriticality.Critical,
            "Merchant",
            await context.CallAsync("Merchant", () => _merchant.GetMySellerAsync(cancellationToken)))!;

        // LA JOURNÉE EST CELLE D'UTC, PAS CELLE DE COTONOU.
        //
        // Le Bénin est à UTC+1 et n'observe aucun changement d'heure : entre
        // minuit et 1 h locale, « aujourd'hui » côté serveur est encore la veille.
        // Le tableau de bord d'un commerçant ouvert tard afficherait donc les
        // chiffres de la veille pendant une heure.
        //
        // LE DÉCALAGE A CHANGÉ DE PROPRIÉTAIRE, ET C'EST UN PROGRÈS. Il était
        // décidé ici, par un `DateTime.UtcNow.Date` que la passerelle ne pouvait
        // pas corriger sans connaître le pays du vendeur ; il est maintenant DANS
        // le service, dans `JourneeAnalytique`, en un seul endroit — et le jour
        // où l'on passera en journées locales, il faudra RECALCULER l'historique,
        // ce qui n'était même pas exprimable avant.
        var aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);
        var debut = aujourdhui.AddDays(-(SalesWindowDays - 1));

        // ── Vague 2 : boutique, courbes, commandes et portefeuille, en parallèle ─
        var storeTask = context.CallAsync(
            "Merchant", () => _merchant.GetStoreAsync(seller.Id, storeId, cancellationToken));

        var salesTask = context.CallAsync(
            "Analytics", () => _analytics.GetSellerSalesAsync(
                seller.Id, debut, aujourdhui, cancellationToken));

        var ordersTask = context.CallAsync(
            "Order", () => _order.ListBySellerAsync(seller.Id, cancellationToken));

        var walletTask = context.CallAsync(
            "Financial", () => _financial.GetSellerWalletAsync(seller.Id, cancellationToken));

        await Task.WhenAll(storeTask, salesTask, ordersTask, walletTask);

        // 404 si la boutique n'appartient pas à ce vendeur — cf. remarques.
        var store = context.Resolve(
            DependencyCriticality.Critical, "Merchant", await storeTask)!;

        var sales = context.Resolve(
            DependencyCriticality.Important, "Analytics", await salesTask);

        var orders = context.Resolve(
            DependencyCriticality.Important, "Order", await ordersTask);

        var wallet = context.Resolve(
            DependencyCriticality.Important, "Financial", await walletTask);

        // LE POINT DU JOUR, CHERCHÉ PAR SA DATE ET NON PRIS EN DERNIER.
        //
        // La série est rendue triée et sans trou, donc `Points[^1]` serait la
        // bonne ligne aujourd'hui. Ce serait vrai par PROPRIÉTÉ D'UN AUTRE
        // SERVICE — et le jour où il rendrait la période à l'envers, ou bornerait
        // au dernier jour ayant une vente, cet écran afficherait les chiffres
        // d'un autre jour sans que rien ne le dise.
        var pointDuJour = sales?.Points.FirstOrDefault(point => point.Day == aujourdhui);

        var commandesATraiter = (orders ?? [])
            .Count(order => ToProcess.Contains(order.Status, StringComparer.OrdinalIgnoreCase));

        var dto = new MerchantDashboardDto(
            Store: new MerchantStoreDto(
                store.Id, store.Name, store.LogoUrl, store.Status, store.IsSelling, store.ContactPhone),
            Today: new MerchantTodayDto(
                OrdersToday: pointDuJour?.OrdersCount,
                RevenueToday: pointDuJour?.Revenue,

                // DIVISION GARDÉE, ET ELLE PORTE SUR LA JOURNÉE.
                //
                // `sales.AverageOrderValue` existe et couvre les TRENTE jours :
                // l'employer ici afficherait le panier moyen du mois sous une
                // tuile qui dit « aujourd'hui ». Un panier moyen sur zéro commande
                // n'est pas « 0 F » — il n'existe pas, et `0m` s'afficherait comme
                // un panier moyen nul un jour sans vente.
                AverageBasket: pointDuJour is null || pointDuJour.OrdersCount == 0
                    ? null
                    : pointDuJour.Revenue / pointDuJour.OrdersCount,

                // La devise vient de la série ; le portefeuille prend le relais
                // quand analytics est muet, comme la liste de commandes le
                // faisait avant lui.
                Currency: sales?.Currency ?? wallet?.Currency,
                OrdersToProcess: commandesATraiter),
            Sales: sales is null ? null : Vers(sales),
            Wallet: wallet is null
                ? null
                : new MerchantWalletDto(
                    wallet.PendingBalance, wallet.AvailableBalance,
                    wallet.PendingWithdrawal, wallet.Currency),
            RecentOrders:
            [
                .. (orders ?? [])
                    .OrderByDescending(order => order.CreatedAtUtc)
                    .Take(RecentOrderCount)
                    .Select(order => new MerchantOrderDto(
                        order.Id, order.Status, order.GrandTotal, order.Currency, order.CreatedAtUtc)),
            ]);

        return context.Complete(dto);
    }

    /// <summary>
    /// Traduit la série du service vers celle de l'écran.
    /// </summary>
    /// <remarks>
    /// PARTAGÉE AVEC `GetMerchantAnalyticsHandler`, ET C'EST VOULU : deux
    /// traductions du même contrat divergeraient au premier champ ajouté, et
    /// l'écran dédié afficherait alors autre chose que la tuile d'accueil.
    /// </remarks>
    internal static MerchantSalesSeriesDto Vers(SellerSalesSeries serie)
        => new(
            serie.From,
            serie.To,
            serie.Currency,
            [.. serie.Points.Select(point =>
                new MerchantSalesPointDto(point.Day, point.OrdersCount, point.Revenue))],
            serie.TotalOrders,
            serie.TotalRevenue,
            serie.AverageOrderValue);
}
