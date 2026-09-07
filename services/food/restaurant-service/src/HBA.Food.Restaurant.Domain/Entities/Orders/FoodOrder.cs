using HBA.Food.Domain.Orders.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Orders;

public readonly record struct FoodOrderId(Guid Value)
{
    public static FoodOrderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Le refus, et ce qui permet d'en rendre compte (cahier §11).</summary>
public sealed record FoodOrderRejection(
    FoodRejectionReason Reason, string? Comment, Guid RejectedByUserId, DateTime RejectedAtUtc);

/// <summary>LA PART OPÉRATIONNELLE D'UNE COMMANDE (cahier des charges §10 à §14).</summary>
public sealed class FoodOrder : AggregateRoot<FoodOrderId>
{
    private readonly List<FoodOrderItem> _items = new();

    private FoodOrder()
    {
    }

    private FoodOrder(
        FoodOrderId id, FoodOrderOrigin origin, Guid orderId, Guid restaurantId,
        string? customerNote, DateTime nowUtc)
        : base(id)
    {
        Origin = origin;
        OrderId = orderId;
        RestaurantId = restaurantId;
        CustomerNote = customerNote;
        Status = FoodOrderStatus.PendingRestaurantAcceptance;
        ReceivedAtUtc = nowUtc;
    }

    /// <summary>De quel univers vient <see cref="OrderId"/>.</summary>
    public FoodOrderOrigin Origin { get; private set; }

    /// <summary>
    /// La commande commerciale — chez Ordering OU chez FoodOrders, selon
    /// <see cref="Origin"/> .
    /// </summary>
    public Guid OrderId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public FoodOrderStatus Status { get; private set; }

    /// <summary>
    /// Instructions du client pour l'ensemble : « sonner fort », « sans couverts ».
    /// </summary>
    public string? CustomerNote { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>Qui a accepté, et quand. Le cahier (§21) demande de tracer l'acceptation.</summary>
    public Guid? AcceptedByUserId { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }

    public FoodOrderRejection? Rejection { get; private set; }

    /// <summary>Première ligne touchée par la cuisine.</summary>
    public DateTime? StartedAtUtc { get; private set; }

    /// <summary>Instant où TOUTES les lignes ont été prêtes — toutes stations confondues.</summary>
    public DateTime? ReadyAtUtc { get; private set; }

    public DateTime? PickedUpAtUtc { get; private set; }

    /// <summary>Délai annoncé à l'acceptation, en minutes.</summary>
    public int? EstimatedPreparationMinutes { get; private set; }

    /// <summary>Priorité d'affichage sur l'écran de cuisine (§12).</summary>
    public int Priority { get; private set; }

    public IReadOnlyCollection<FoodOrderItem> Items => _items.AsReadOnly();

    /// <summary>L'état du ticket, DÉRIVÉ de ses lignes — jamais stocké.</summary>
    public KitchenTicketStatus KitchenStatus
    {
        get
        {
            if (Status is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected)
            {
                return KitchenTicketStatus.Cancelled;
            }

            if (_items.Count > 0 && _items.All(i => i.Status == KitchenItemStatus.Ready))
            {
                return KitchenTicketStatus.Ready;
            }

            return _items.Any(i => i.Status != KitchenItemStatus.Pending)
                ? KitchenTicketStatus.Preparing
                : KitchenTicketStatus.Pending;
        }
    }

    /// <summary>Le total de la part restauration.</summary>
    public decimal Total => _items.Sum(i => i.LineTotal);

    /// <summary>Les postes concernés par cette commande.</summary>
    public IReadOnlyCollection<Guid> Stations
        => _items.Where(i => i.PreparationStationId is not null)
            .Select(i => i.PreparationStationId!.Value)
            .Distinct()
            .ToList();

    // ── Réception ───────────────────────────────────────────────────────────

    /// <summary>Une commande arrive du module Ordering.</summary>
    public static Result<FoodOrder> Receive(
        FoodOrderOrigin origin,
        Guid orderId,
        Guid restaurantId,
        IReadOnlyList<FoodOrderItem> items,
        string? customerNote,
        DateTime nowUtc)
    {
        if (orderId == Guid.Empty || restaurantId == Guid.Empty)
        {
            return Error.Validation(
                "food.order.parent_required", "La commande doit référencer une commande et un restaurant.");
        }

        if (items.Count == 0)
        {
            // Une commande vide n'apparaîtrait sur aucun écran de cuisine,
            // resterait « à accepter » pour toujours, et personne ne saurait dire
            // pourquoi.
            return Error.Validation("food.order.empty", "Une commande sans article n'a pas de sens.");
        }

        var commande = new FoodOrder(
            FoodOrderId.New(), origin, orderId, restaurantId,
            string.IsNullOrWhiteSpace(customerNote) ? null : customerNote.Trim(), nowUtc);

        commande._items.AddRange(items);

        // L'origine voyage avec le fait : tout ce qui écoutera ce ticket devra
        // savoir à quelle base poser ses questions, et le rappeler à chaque
        // gestionnaire par une lecture en base serait un aller-retour de plus pour
        // une donnée qui ne change jamais.
        commande.Raise(new FoodOrderReceivedDomainEvent(
            commande.Id.Value, origin, orderId, restaurantId, commande.Total, items.Count));

        return commande;
    }

    // ── Décision du restaurant ──────────────────────────────────────────────

    /// <summary>Le restaurant accepte : le ticket de cuisine existe (§12).</summary>
    public Result Accept(Guid actorUserId, DateTime nowUtc, int extraWaitMinutes = 0)
        => AcceptInternal(actorUserId, nowUtc, extraWaitMinutes);

    /// <summary>Acceptation AUTOMATIQUE (§3, mode <c>Automatic</c>).</summary>
    public Result AcceptAutomatically(DateTime nowUtc, int extraWaitMinutes = 0)
    {
        var result = AcceptInternal(actorUserId: null, nowUtc, extraWaitMinutes);
        if (result.IsSuccess)
        {
            WasAutoAccepted = true;
        }

        return result;
    }

    /// <summary>Acceptée sans qu'aucun humain ne l'ait regardée.</summary>
    public bool WasAutoAccepted { get; private set; }

    private Result AcceptInternal(Guid? actorUserId, DateTime nowUtc, int extraWaitMinutes)
    {
        if (Status == FoodOrderStatus.Accepted)
        {
            // Idempotent : deux caissiers sur le même écran ne doivent pas produire
            // une erreur incompréhensible pour le second.
            return Result.Success();
        }

        if (Status != FoodOrderStatus.PendingRestaurantAcceptance)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_pending",
                Status == FoodOrderStatus.Cancelled
                    ? "Cette commande a été annulée : elle ne peut plus être acceptée."
                    : "Cette commande n'attend plus de décision du restaurant."));
        }

        Status = FoodOrderStatus.Accepted;
        AcceptedByUserId = actorUserId;
        AcceptedAtUtc = nowUtc;

        // §14 : ETA = MAX(temps des articles) + temps d'attente estimé.
        EstimatedPreparationMinutes = _items.Max(i => i.PreparationMinutes) + Math.Max(0, extraWaitMinutes);

        Raise(new FoodOrderAcceptedDomainEvent(
            Id.Value, Origin, OrderId, RestaurantId, actorUserId, EstimatedPreparationMinutes.Value));

        return Result.Success();
    }

    /// <summary>Le restaurant refuse, avec un motif (§11).</summary>
    public Result Reject(Guid actorUserId, FoodRejectionReason reason, string? comment, DateTime nowUtc)
    {
        if (Status != FoodOrderStatus.PendingRestaurantAcceptance)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_pending", "Cette commande n'attend plus de décision du restaurant."));
        }

        Status = FoodOrderStatus.Rejected;
        Rejection = new FoodOrderRejection(
            reason, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), actorUserId, nowUtc);

        Raise(new FoodOrderRejectedDomainEvent(
            Id.Value, Origin, OrderId, RestaurantId, reason.ToString(), Rejection.Comment, actorUserId));

        return Result.Success();
    }

    // ── Cuisine ─────────────────────────────────────────────────────────────

    /// <summary>Le cuisinier commence une ligne.</summary>
    public Result StartItem(Guid itemId, DateTime nowUtc)
        => OnKitchenItem(itemId, ligne => ligne.Start(), nowUtc);

    /// <summary>« Commencer » sur tout le ticket — le bouton du §13.</summary>
    public Result StartAll(DateTime nowUtc)
    {
        var garde = EnsureKitchenOpen();
        if (garde.IsFailure)
        {
            return garde;
        }

        foreach (var ligne in _items)
        {
            ligne.Start();
        }

        EnterPreparation(nowUtc);
        return Result.Success();
    }

    /// <summary>Une ligne est prête. Les autres postes peuvent encore travailler.</summary>
    public Result MarkItemReady(Guid itemId, DateTime nowUtc)
        => OnKitchenItem(itemId, ligne => ligne.MarkReady(), nowUtc);

    /// <summary>
    /// Une ligne repart en préparation — plat renversé, marquée prête par erreur.
    /// </summary>
    public Result ReopenItem(Guid itemId, DateTime nowUtc)
        => OnKitchenItem(itemId, ligne => ligne.Reopen(), nowUtc);

    /// <summary>Tout le ticket est prêt.</summary>
    public Result MarkAllReady(DateTime nowUtc)
    {
        var garde = EnsureKitchenOpen();
        if (garde.IsFailure)
        {
            return garde;
        }

        foreach (var ligne in _items)
        {
            ligne.MarkReady();
        }

        EnterPreparation(nowUtc);
        SettleReadiness(nowUtc);

        return Result.Success();
    }

    /// <summary>Remonte ou redescend la commande sur l'écran de cuisine.</summary>
    public Result SetPriority(int priority)
    {
        Priority = priority;
        return Result.Success();
    }

    // ── Sortie ──────────────────────────────────────────────────────────────

    /// <summary>Le livreur emporte le sac.</summary>
    public Result MarkPickedUp(DateTime nowUtc)
    {
        if (Status == FoodOrderStatus.PickedUp)
        {
            return Result.Success();
        }

        if (Status != FoodOrderStatus.ReadyForPickup)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_ready", "Cette commande n'est pas prête à être enlevée."));
        }

        Status = FoodOrderStatus.PickedUp;
        PickedUpAtUtc = nowUtc;

        Raise(new FoodOrderPickedUpDomainEvent(Id.Value, Origin, OrderId, RestaurantId));
        return Result.Success();
    }

    /// <summary>Le repas est entre les mains du client.</summary>
    public Result MarkDelivered()
    {
        if (Status == FoodOrderStatus.Delivered)
        {
            // Idempotent : la fin de course peut être rejouée par l'outbox.
            return Result.Success();
        }

        if (Status != FoodOrderStatus.PickedUp)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_picked_up", "Cette commande n'a pas encore été enlevée."));
        }

        Status = FoodOrderStatus.Delivered;

        Raise(new FoodOrderDeliveredDomainEvent(Id.Value, Origin, OrderId, RestaurantId));
        return Result.Success();
    }

    /// <summary>La commande est annulée — par le client, l'exploitation, ou un incident.</summary>
    public Result Cancel(string? reason)
    {
        // « DÉJÀ REFUSÉE » EST UN SUCCÈS, AU MÊME TITRE QUE « DÉJÀ ANNULÉE ».
        if (Status is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected)
        {
            return Result.Success();
        }

        if (Status is FoodOrderStatus.PickedUp or FoodOrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_cancellable", "Cette commande ne peut plus être annulée."));
        }

        var etaitEnCuisine = Status is FoodOrderStatus.Accepted
            or FoodOrderStatus.Preparing or FoodOrderStatus.ReadyForPickup;

        Status = FoodOrderStatus.Cancelled;

        // Le ticket dérive déjà en « annulé » ; l'événement porte l'information qui
        // COMPTE pour le restaurant : y avait-il des denrées engagées ?
        Raise(new FoodOrderCancelledDomainEvent(
            Id.Value, Origin, OrderId, RestaurantId, reason, etaitEnCuisine));

        return Result.Success();
    }

    // ── Mécanique interne ───────────────────────────────────────────────────

    /// <summary>La cuisine peut-elle travailler sur cette commande ?</summary>
    private Result EnsureKitchenOpen()
    {
        if (Status is FoodOrderStatus.Accepted or FoodOrderStatus.Preparing or FoodOrderStatus.ReadyForPickup)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Conflict(
            "food.order.kitchen_closed",
            Status == FoodOrderStatus.PendingRestaurantAcceptance
                ? "Cette commande n'a pas encore été acceptée."
                : "Cette commande n'est plus en cuisine."));
    }

    private Result OnKitchenItem(Guid itemId, Func<FoodOrderItem, bool> action, DateTime nowUtc)
    {
        var garde = EnsureKitchenOpen();
        if (garde.IsFailure)
        {
            return garde;
        }

        var ligne = _items.FirstOrDefault(i => i.Id == itemId);
        if (ligne is null)
        {
            return Result.Failure(Error.NotFound("food.order.item_not_found", "Ligne introuvable sur cette commande."));
        }

        action(ligne);

        EnterPreparation(nowUtc);
        SettleReadiness(nowUtc);

        return Result.Success();
    }

    /// <summary>LA COMMANDE PASSE EN PRÉPARATION DÈS QUE LA CUISINE TOUCHE UNE LIGNE.</summary>
    private void EnterPreparation(DateTime nowUtc)
    {
        if (_items.All(i => i.Status == KitchenItemStatus.Pending))
        {
            return;
        }

        StartedAtUtc ??= nowUtc;

        if (Status == FoodOrderStatus.Accepted)
        {
            Status = FoodOrderStatus.Preparing;
            Raise(new FoodOrderPreparationStartedDomainEvent(Id.Value, Origin, OrderId, RestaurantId));
        }
    }

    /// <summary>
    /// « La commande globale est prête si TOUTES les stations sont READY » (§13).
    /// </summary>
    private void SettleReadiness(DateTime nowUtc)
    {
        var toutEstPret = _items.Count > 0 && _items.All(i => i.Status == KitchenItemStatus.Ready);

        if (toutEstPret && Status == FoodOrderStatus.Preparing)
        {
            Status = FoodOrderStatus.ReadyForPickup;
            ReadyAtUtc = nowUtc;

            // C'EST CET ÉVÉNEMENT QUI APPELLE UN LIVREUR. Le §24 le place au centre
            // du flux : ReadyForPickup → HBA Delivery → HBA Driver.
            Raise(new FoodOrderReadyForPickupDomainEvent(
                Id.Value, Origin, OrderId, RestaurantId, ReadyAtUtc.Value));
            return;
        }

        if (!toutEstPret && Status == FoodOrderStatus.ReadyForPickup)
        {
            Status = FoodOrderStatus.Preparing;
            ReadyAtUtc = null;
        }
    }
}

/// <summary>Accès aux commandes Food.</summary>
public interface IFoodOrderRepository
{
    Task<FoodOrder?> GetByIdAsync(FoodOrderId id, CancellationToken cancellationToken = default);

    /// <summary>Par la commande commerciale.</summary>
    /// <summary>Le ticket d'une commande, DANS SON UNIVERS.</summary>
    Task<FoodOrder?> GetByOrderIdAsync(
        FoodOrderOrigin origin, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le tableau de cuisine : ce qui est encore en jeu, du plus prioritaire au
    /// plus ancien.
    /// </summary>
    Task<IReadOnlyList<FoodOrder>> ListActiveAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FoodOrder>> ListByStatusAsync(
        Guid restaurantId, FoodOrderStatus status, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commandes en cours, pour la saturation (§14 : <c> MaximumActiveOrders</c>).
    /// </summary>
    Task<int> CountActiveAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task AddAsync(FoodOrder order, CancellationToken cancellationToken = default);
}
