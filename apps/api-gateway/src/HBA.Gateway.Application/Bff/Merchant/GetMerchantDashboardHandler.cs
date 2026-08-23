using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
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
/// CRITICITÉ (§23)
///   Merchant · vendeur   CRITIQUE   — sans sellerId, rien n'est interrogeable.
///   Merchant · boutique  CRITIQUE   — c'est le sujet de l'écran ; 404 ⇒ 404.
///   Order                IMPORTANTE — les chiffres manquent, l'écran vit.
///   Financial            IMPORTANTE — le solde manque, l'écran vit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetMerchantDashboardHandler
{
    public const string ScreenId = "merchant.store_dashboard";

    /// <summary>Commandes récentes affichées.</summary>
    public const int RecentOrderCount = 5;

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

    public GetMerchantDashboardHandler(
        IMerchantClient merchant, IOrderClient order, IFinancialClient financial)
    {
        _merchant = merchant;
        _order = order;
        _financial = financial;
    }

    public async Task<BffEnvelope<MerchantDashboardDto>> HandleAsync(
        Guid storeId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var seller = context.Resolve(
            DependencyCriticality.Critical,
            "Merchant",
            await context.CallAsync("Merchant", () => _merchant.GetMySellerAsync(cancellationToken)))!;

        // ── Vague 2 : boutique, commandes et portefeuille, tous en parallèle ─
        var storeTask = context.CallAsync(
            "Merchant", () => _merchant.GetStoreAsync(seller.Id, storeId, cancellationToken));

        var ordersTask = context.CallAsync(
            "Order", () => _order.ListBySellerAsync(seller.Id, cancellationToken));

        var walletTask = context.CallAsync(
            "Financial", () => _financial.GetSellerWalletAsync(seller.Id, cancellationToken));

        await Task.WhenAll(storeTask, ordersTask, walletTask);

        // 404 si la boutique n'appartient pas à ce vendeur — cf. remarques.
        var store = context.Resolve(
            DependencyCriticality.Critical, "Merchant", await storeTask)!;

        var orders = context.Resolve(
            DependencyCriticality.Important, "Order", await ordersTask);

        var wallet = context.Resolve(
            DependencyCriticality.Important, "Financial", await walletTask);

        // LA JOURNÉE EST CELLE D'UTC, PAS CELLE DE COTONOU.
        //
        // Le Bénin est à UTC+1 et n'observe aucun changement d'heure : entre
        // minuit et 1 h locale, « aujourd'hui » côté serveur est encore la veille.
        // Le tableau de bord d'un commerçant ouvert tard afficherait donc zéro
        // pendant une heure. À corriger quand le fuseau sera porté par le service —
        // le calculer ici supposerait que la passerelle connaisse le pays.
        var today = DateTime.UtcNow.Date;

        var ofToday = (orders ?? [])
            .Where(order => order.CreatedAtUtc.Date == today)
            .ToList();

        var revenue = ofToday.Sum(order => order.GrandTotal);

        var dto = new MerchantDashboardDto(
            Store: new MerchantStoreDto(
                store.Id, store.Name, store.LogoUrl, store.Status, store.IsSelling, store.ContactPhone),
            Today: new MerchantTodayDto(
                OrdersToday: ofToday.Count,
                RevenueToday: revenue,
                // DIVISION GARDÉE. Un panier moyen sur zéro commande n'est pas
                // « 0 F » : il n'existe pas, et `0m` s'afficherait comme un
                // panier moyen nul un jour sans vente.
                AverageBasket: ofToday.Count == 0 ? null : revenue / ofToday.Count,
                Currency: ofToday.FirstOrDefault()?.Currency ?? wallet?.Currency,
                OrdersToProcess: (orders ?? [])
                    .Count(order => ToProcess.Contains(order.Status, StringComparer.OrdinalIgnoreCase))),
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
}
