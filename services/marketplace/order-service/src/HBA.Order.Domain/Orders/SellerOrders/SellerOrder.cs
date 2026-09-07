using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Orders.Domain.Orders.SellerOrders.Events;

// Même alias que `PlaceOrderCommandHandler` et `OrderLifecycleCommands` : sous
// l'espace englobant `HBA.Orders.…`, « Order » se résout mal, et le compilateur ne
// le signale qu'à la ligne suivante, sur une conversion impossible.
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>
/// La part d'UN vendeur dans une commande, avec l'état de ce que CE vendeur doit
/// faire.
/// </summary>
public sealed class SellerOrder : AggregateRoot<SellerOrderId>
{
    private readonly List<SellerOrderLine> _lines = new();

    private SellerOrder()
    {
    }

    private SellerOrder(
        SellerOrderId id, Guid orderId, Guid sellerId, Guid buyerId, string currency, DateTime nowUtc)
        : base(id)
    {
        OrderId = orderId;
        SellerId = sellerId;
        BuyerId = buyerId;
        Currency = currency;
        Status = SellerOrderStatus.AwaitingConfirmation;
        CreatedAtUtc = nowUtc;
    }

    /// <summary>La commande dont ceci est une part.</summary>
    public Guid OrderId { get; private set; }

    public Guid SellerId { get; private set; }

    /// <summary>L'acheteur, RECOPIÉ depuis la commande.</summary>
    public Guid BuyerId { get; private set; }

    /// <summary>Devise de la commande, recopiée.</summary>
    public string Currency { get; private set; } = default!;

    public SellerOrderStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    // LES HORODATAGES DE TRANSITION.
    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? PreparingAtUtc { get; private set; }

    public DateTime? ReadyForPickupAtUtc { get; private set; }

    public DateTime? HandedOverAtUtc { get; private set; }

    public DateTime? RefusedAtUtc { get; private set; }

    /// <summary>Pourquoi cette part ne sera pas honorée.</summary>
    public string? RefusalReason { get; private set; }

    public IReadOnlyCollection<SellerOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>Nombre d'articles de cette part (somme des quantités).</summary>
    public int ItemCount => _lines.Sum(l => l.Quantity);

    /// <summary>Montant PAYÉ pour cette part, remises comprises.</summary>
    public decimal Amount => _lines.Sum(l => l.LineTotal);

    /// <summary>Cette part attend-elle encore un geste du vendeur ?</summary>
    public bool IsOpen => Status is not (SellerOrderStatus.HandedOver
        or SellerOrderStatus.Rejected
        or SellerOrderStatus.Cancelled);

    /// <summary>Découpe une commande CONFIRMÉE en une part par vendeur.</summary>
    public static Result<IReadOnlyList<SellerOrder>> SplitFrom(OrderAggregate order, DateTime nowUtc)
    {
        // ON EXIGE « CONFIRMÉE », ET C'EST L'INVARIANT DE NAISSANCE.
        if (order.Status != OrderStatus.Confirmed)
        {
            return Error.Conflict(
                "ordering.seller_order.order_not_confirmed",
                "Une commande vendeur ne se crée qu'à la confirmation de la commande.");
        }

        var parts = new List<SellerOrder>();

        foreach (var groupe in order.SellerLineGroups())
        {
            var part = new SellerOrder(
                SellerOrderId.New(), order.Id.Value, groupe.Key, order.BuyerId, order.Currency, nowUtc);

            foreach (var ligne in groupe)
            {
                part._lines.Add(new SellerOrderLine(
                    Guid.NewGuid(),
                    ligne.Id,
                    ligne.ProductId,
                    ligne.Sku,
                    ligne.ShipFromLocationId,
                    ligne.Quantity,
                    ligne.FinalUnitPrice));
            }

            parts.Add(part);
        }

        return Result.Success<IReadOnlyList<SellerOrder>>(parts);
    }

    /// <summary>Le vendeur s'engage à honorer sa part.</summary>
    public Result Confirm(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.AwaitingConfirmation)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Cette commande vendeur n'attend plus de confirmation."));
        }

        Status = SellerOrderStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Le vendeur REFUSE sa part, avant de s'être engagé.</summary>
    public Result Reject(string reason, DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.AwaitingConfirmation)
        {
            // MESSAGE QUI DÉSIGNE L'AUTRE GESTE, PARCE QUE L'AUTRE GESTE EXISTE.
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.already_engaged",
                "Cette commande a déjà été confirmée : elle ne se refuse plus, elle s'annule."));
        }

        return Refuser(SellerOrderStatus.Rejected, "Rejected", reason, nowUtc);
    }

    /// <summary>Le colis se monte.</summary>
    public Result MarkPreparing(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Seule une commande vendeur confirmée peut passer en préparation."));
        }

        Status = SellerOrderStatus.Preparing;
        PreparingAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Le colis attend le livreur.</summary>
    public Result MarkReadyForPickup(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.Preparing)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Seule une commande vendeur en préparation peut être déclarée prête."));
        }

        Status = SellerOrderStatus.ReadyForPickup;
        ReadyForPickupAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Le colis a quitté le vendeur.</summary>
    public Result MarkHandedOver(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.ReadyForPickup)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Seule une commande vendeur prête peut être remise au livreur."));
        }

        Status = SellerOrderStatus.HandedOver;
        HandedOverAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Le vendeur se dédit APRÈS s'être engagé : rupture découverte à l'emballage,
    /// casse, article introuvable.
    /// </summary>
    public Result Cancel(string reason, DateTime nowUtc)
    {
        if (Status == SellerOrderStatus.AwaitingConfirmation)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.not_yet_engaged",
                "Cette commande n'a pas encore été confirmée : elle ne s'annule pas, elle se refuse."));
        }

        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.already_closed",
                "Cette commande vendeur n'est plus annulable dans cet état."));
        }

        return Refuser(SellerOrderStatus.Cancelled, "Cancelled", reason, nowUtc);
    }

    /// <summary>La COMMANDE ENTIÈRE a été annulée : la part du vendeur tombe avec elle.</summary>
    public Result CancelWithOrder(string reason, DateTime nowUtc)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.already_closed",
                "Cette commande vendeur est déjà close."));
        }

        Status = SellerOrderStatus.Cancelled;
        RefusedAtUtc = nowUtc;
        RefusalReason = Motif(reason, "Commande annulée.");
        return Result.Success();
    }

    /// <summary>Le corps commun aux deux refus : même écriture, même événement.</summary>
    private Result Refuser(SellerOrderStatus statut, string issue, string reason, DateTime nowUtc)
    {
        // VALIDATION, PAS CONFLIT : un motif vide est une requête mal formée, pas
        // un état incompatible.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "ordering.seller_order.reason_required",
                "Un motif est obligatoire : c'est la seule trace de pourquoi cette commande payée ne sera pas honorée."));
        }

        var motif = Motif(reason, string.Empty);

        Status = statut;
        RefusedAtUtc = nowUtc;
        RefusalReason = motif;

        Raise(new SellerOrderRefusedDomainEvent(
            Id.Value,
            OrderId,
            BuyerId,
            SellerId,
            Currency,
            issue,
            motif,
            Amount,
            _lines
                .Select(l => new SellerOrderRefusedLine(
                    l.OrderLineId, l.ProductId, l.Sku, l.ShipFromLocationId, l.Quantity, l.LineTotal))
                .ToList()));

        return Result.Success();
    }

    /// <summary>
    /// Borne le motif à ce que la colonne accepte : tronquer vaut mieux que perdre.
    /// </summary>
    private static string Motif(string reason, string defaut)
    {
        var propre = string.IsNullOrWhiteSpace(reason) ? defaut : reason.Trim();
        return propre.Length > 500 ? propre[..500] : propre;
    }
}
