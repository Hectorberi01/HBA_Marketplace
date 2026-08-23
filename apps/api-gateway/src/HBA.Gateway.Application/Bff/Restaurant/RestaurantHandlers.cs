using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Food;

namespace HBA.Gateway.Application.Bff.Restaurant;

/// <summary>
/// Statuts de ticket, regroupés comme le KDS les affiche.
/// </summary>
/// <remarks>
/// TROIS COLONNES, ET LE REGROUPEMENT EST FAIT ICI.
///
/// food-service rend une liste plate avec un statut par ticket. Le §14 demande
/// trois seaux — pending / preparing / ready. Les constituer côté client
/// obligerait chaque application à connaître la liste des statuts, et à la
/// maintenir en même temps que le service.
///
/// CE SONT DES `KitchenTicketStatus`, PAS DES `FoodOrderStatus`.
///
/// Deux énumérations coexistent dans food-service et se ressemblent assez pour
/// qu'on les confonde :
///
///   • <c>FoodOrderStatus</c> — le cycle de vie commercial de la commande :
///     PendingRestaurantAcceptance, Accepted, Rejected, Preparing, ReadyForPickup,
///     PickedUp, Delivered.
///   • <c>KitchenTicketStatus</c> — l'avancement en cuisine, DÉRIVÉ des lignes :
///     Pending, Preparing, Ready, Cancelled.
///
/// <c>KitchenTicketView.Status</c> porte le SECOND (<c>KitchenStatus.ToString()</c>
/// dans <c>GetKitchenBoardQueryHandler</c>). Ranger « Accepted » ou
/// « ReadyForPickup » ici ne lèverait aucune erreur : les tickets tomberaient
/// simplement dans aucun seau, et l'écran de cuisine s'afficherait vide alors que
/// le service répond correctement.
///
/// « Cancelled » n'a pas de seau : le service écarte déjà les commandes annulées
/// avant de construire le tableau. Un ticket annulé qui arriverait tout de même
/// disparaîtrait — c'est le comportement voulu, la cuisine doit s'arrêter.
/// </remarks>
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

/// <summary>
/// Tableau de bord du restaurant (§13).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉTABLISSEMENT VIENT DU JETON, PAS DE L'URL — MÊME QUAND L'URL EN A UN.
///
/// La route expose <c>/restaurants/{id}/dashboard</c> pour rester lisible et
/// alignée sur le §13. Mais l'identifiant utilisé pour agréger est celui rendu
/// par <c>GET /api/food/partner/me</c>, résolu depuis le jeton. Celui de l'URL
/// n'est qu'une VÉRIFICATION : s'il ne correspond pas, c'est 404.
///
/// Faire l'inverse — faire confiance à l'URL — laisserait un caissier lire le
/// tableau de bord d'un autre restaurant en changeant un identifiant.
///
/// CRITICITÉ (§23)
///   Food · établissement  CRITIQUE   — c'est le sujet de l'écran.
///   Food · cuisine        IMPORTANTE — les compteurs manquent, l'écran vit.
///   Financial            OPTIONNELLE — un cuisinier n'a pas à voir le solde ;
///                                      son absence n'est pas une dégradation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetRestaurantDashboardHandler
{
    public const string ScreenId = "restaurant.dashboard";

    /// <summary>
    /// Permission requise pour lire le portefeuille.
    /// </summary>
    /// <remarks>
    /// VALEUR À CONFIRMER CONTRE LE MODÈLE DE PERMISSIONS DE food-service.
    ///
    /// Les codes existent côté service (<c>FoodStaffMembership.Can</c>) mais ne
    /// sont pas exposés en constante partagée. Si celui-ci ne correspond à rien,
    /// le solde ne s'affichera JAMAIS — un défaut silencieux, d'où cette note.
    /// </remarks>
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
        //
        // Filtrer la réponse après l'avoir reçue laisserait le montant transiter
        // sur le réseau et apparaître dans les journaux de la passerelle. Ne pas
        // émettre l'appel est la seule forme qui ne fuit rien.
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

/// <summary>
/// Écran de cuisine (§14).
/// </summary>
/// <remarks>
/// UNE SEULE DÉPENDANCE, ET C'EST LE POINT.
///
/// Le KDS n'interroge que food-service. Aucun appel à Financial, aucun à
/// Merchant : ce qui n'est pas demandé ne peut pas fuiter sur une tablette de
/// cuisine.
/// </remarks>
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
                // Le plus ancien EN TÊTE : une cuisine sert dans l'ordre
                // d'arrivée, et un tri décroissant ferait passer la dernière
                // commande avant celle qui attend depuis vingt minutes.
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
