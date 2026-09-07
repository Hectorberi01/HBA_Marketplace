using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Analytics;
using HBA.Gateway.Application.Contracts.Order;

namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>Tableau de bord d'une boutique (§12).</summary>
public sealed class GetMerchantDashboardHandler
{
    public const string ScreenId = "merchant.store_dashboard";

    /// <summary>Commandes récentes affichées.</summary>
    public const int RecentOrderCount = 5;

    /// <summary>Profondeur de la courbe du tableau de bord, en jours.</summary>
    public const int SalesWindowDays = 30;

    /// <summary>Statuts d'une commande qui attend une action du vendeur.</summary>
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

        // 404 si la boutique n'appartient pas à ce vendeur — cf.
        var store = context.Resolve(
            DependencyCriticality.Critical, "Merchant", await storeTask)!;

        var sales = context.Resolve(
            DependencyCriticality.Important, "Analytics", await salesTask);

        var orders = context.Resolve(
            DependencyCriticality.Important, "Order", await ordersTask);

        var wallet = context.Resolve(
            DependencyCriticality.Important, "Financial", await walletTask);

        // LE POINT DU JOUR, CHERCHÉ PAR SA DATE ET NON PRIS EN DERNIER.
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
                AverageBasket: pointDuJour is null || pointDuJour.OrdersCount == 0
                    ? null
                    : pointDuJour.Revenue / pointDuJour.OrdersCount,

                // La devise vient de la série ; le portefeuille prend le relais
                // quand analytics est muet, comme la liste de commandes le faisait
                // avant lui.
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

    /// <summary>Traduit la série du service vers celle de l'écran.</summary>
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
