using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Food;

namespace HBA.Gateway.Application.Bff.Restaurant;

/// <summary>Statuts de ticket, regroupés comme le KDS les affiche.</summary>
internal static class KitchenBuckets
{
    /// <summary>Accepté, aucun article commencé.</summary>
    internal static readonly string[] Pending = ["Pending"];

    /// <summary>Au moins un article commencé, tous ne sont pas prêts.</summary>
    internal static readonly string[] Preparing = ["Preparing"];

    /// <summary>Tous les articles prêts, toutes stations confondues.</summary>
    internal static readonly string[] Ready = ["Ready"];

    internal static bool In(string[] bucket, string status)
        => bucket.Contains(status, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Tableau de bord du restaurant (§13).</summary>
public sealed class GetRestaurantDashboardHandler
{
    public const string ScreenId = "restaurant.dashboard";

    /// <summary>Permission requise pour lire le portefeuille.</summary>
    public const string FinancePermission = "Finance.Read";

    private readonly IFoodClient _food;
    private readonly IFinancialClient _financial;

    public GetRestaurantDashboardHandler(IFoodClient food, IFinancialClient financial)
    {
        _food = food;
        _financial = financial;
    }

    public async Task<BffEnvelope<RestaurantDashboardDto>> HandleAsync(
        Guid restaurantId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var me = context.Resolve(
            DependencyCriticality.Critical,
            "Food",
            await context.CallAsync("Food", () => _food.GetMyRestaurantAsync(cancellationToken)))!;

        if (me.RestaurantId != restaurantId)
        {
            // « Introuvable » et non « interdit » : un 403 confirmerait que
            // l'établissement existe et appartient à quelqu'un d'autre.
            throw new BffResourceNotFoundException("Restaurant", restaurantId);
        }

        var kitchenTask = context.CallAsync(
            "Food", () => _food.GetKitchenAsync(me.RestaurantId, cancellationToken));

        // LE PORTEFEUILLE N'EST MÊME PAS DEMANDÉ SANS LA PERMISSION.
        var canReadFinance = me.PayoutSellerId is not null
            && me.Permissions.Contains(FinancePermission, StringComparer.Ordinal);

        var walletTask = canReadFinance
            ? context.CallAsync(
                "Financial",
                () => _financial.GetSellerWalletAsync(me.PayoutSellerId!.Value, cancellationToken))
            : null;

        await Task.WhenAll(walletTask is null ? [kitchenTask] : new Task[] { kitchenTask, walletTask });

        var kitchen = context.Resolve(
            DependencyCriticality.Important, "Food", await kitchenTask);

        var wallet = walletTask is null
            ? null
            : context.Resolve(DependencyCriticality.Optional, "Financial", await walletTask);

        var tickets = kitchen?.Tickets ?? [];

        var dto = new RestaurantDashboardDto(
            Restaurant: new RestaurantHeaderDto(
                me.RestaurantId, me.Name, me.Status, me.Role, me.Permissions),
            Service: new RestaurantServiceDto(me.AcceptsOrdersNow, me.BlockedReason),
            Wallet: wallet is null
                ? null
                : new RestaurantWalletDto(
                    wallet.PendingBalance, wallet.AvailableBalance,
                    wallet.PendingWithdrawal, wallet.Currency),
            Kitchen: new RestaurantKitchenSummaryDto(
                Pending: tickets.Count(t => KitchenBuckets.In(KitchenBuckets.Pending, t.Status)),
                Preparing: tickets.Count(t => KitchenBuckets.In(KitchenBuckets.Preparing, t.Status)),
                Ready: tickets.Count(t => KitchenBuckets.In(KitchenBuckets.Ready, t.Status))));

        return context.Complete(dto);
    }
}

/// <summary>Écran de cuisine (§14).</summary>
public sealed class GetRestaurantKitchenHandler
{
    public const string ScreenId = "restaurant.kitchen";

    private readonly IFoodClient _food;

    public GetRestaurantKitchenHandler(IFoodClient food) => _food = food;

    public async Task<BffEnvelope<RestaurantKitchenDto>> HandleAsync(
        Guid restaurantId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var me = context.Resolve(
            DependencyCriticality.Critical,
            "Food",
            await context.CallAsync("Food", () => _food.GetMyRestaurantAsync(cancellationToken)))!;

        if (me.RestaurantId != restaurantId)
        {
            throw new BffResourceNotFoundException("Restaurant", restaurantId);
        }

        var board = context.Resolve(
            DependencyCriticality.Critical,
            "Food",
            await context.CallAsync("Food", () => _food.GetKitchenAsync(restaurantId, cancellationToken)))!;

        var now = DateTime.UtcNow;

        var dto = new RestaurantKitchenDto(
            board.RestaurantId,
            board.StationId,
            [.. board.Stations.Select(s => new KitchenStationDto(s.Id, s.Name, s.IsActive))],
            Bucket(board.Tickets, KitchenBuckets.Pending, now),
            Bucket(board.Tickets, KitchenBuckets.Preparing, now),
            Bucket(board.Tickets, KitchenBuckets.Ready, now));

        return context.Complete(dto);
    }

    private static IReadOnlyList<KitchenTicketDto> Bucket(
        IReadOnlyList<KitchenTicket> tickets, string[] statuses, DateTime nowUtc)
        =>
        [
            .. tickets
                .Where(ticket => KitchenBuckets.In(statuses, ticket.Status))
                // Le plus ancien EN TÊTE : une cuisine sert dans l'ordre d'arrivée,
                // et un tri décroissant ferait passer la dernière commande avant
                // celle qui attend depuis vingt minutes.
                .OrderByDescending(ticket => ticket.Priority)
                .ThenBy(ticket => ticket.ReceivedAtUtc)
                .Select(ticket => new KitchenTicketDto(
                    ticket.FoodOrderId,
                    ticket.OrderId,
                    ticket.Status,
                    ticket.Priority,
                    ticket.EstimatedPreparationMinutes,
                    ticket.ReceivedAtUtc,
                    // Jamais négatif : une horloge de service en avance donnerait
                    // un temps d'attente négatif, affiché tel quel sur la tablette.
                    (int)Math.Max(0, (nowUtc - ticket.ReceivedAtUtc).TotalSeconds),
                    ticket.CustomerNote,
                    ticket.OtherStationsPending,
                    [
                        .. ticket.Items.Select(item => new KitchenTicketItemDto(
                            item.Id, item.Name, item.Quantity, item.Notes, item.Status,
                            item.PreparationStationId, item.PreparationMinutes, item.Options)),
                    ])),
        ];
}
