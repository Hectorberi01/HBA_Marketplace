using HBA.Shared.Domain.Primitives;

namespace HBA.Inventory.Domain.Stock;

/// <summary>
/// Réservation temporaire de stock à la commande, libérée si le paiement échoue
/// (cf.
/// </summary>
public sealed class StockReservation : Entity<Guid>
{
    private StockReservation()
    {
    }

    internal StockReservation(Guid id, Guid orderId, int quantity, DateTime expiresAtUtc)
        : base(id)
    {
        OrderId = orderId;
        Quantity = quantity;
        ExpiresAtUtc = expiresAtUtc;
        Status = ReservationStatus.Active;
    }

    public Guid OrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Où en est cette réservation.</summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>Instant de la confirmation (vente).</summary>
    public DateTime? ConfirmedAtUtc { get; private set; }

    /// <summary>Instant de la libération volontaire (annulation, paiement refusé).</summary>
    public DateTime? ReleasedAtUtc { get; private set; }

    /// <summary>Instant où le balayage a constaté l'expiration.</summary>
    public DateTime? ExpiredAtUtc { get; private set; }

    /// <summary>
    /// Immobilise-t-elle encore du stock ? Seul l'état qui compte pour `Reserved`.
    /// </summary>
    public bool IsActive => Status == ReservationStatus.Active;

    /// <summary>Active ET dépassée : le balayage doit la reprendre.</summary>
    public bool IsExpirableAt(DateTime nowUtc) => IsActive && ExpiresAtUtc <= nowUtc;

    /// <summary>
    /// Rejeu de la même commande sur le même article : on POSE la quantité, on n'en
    /// ajoute pas une seconde (ISSUE-075).
    /// </summary>
    internal void Restate(int quantity, DateTime expiresAtUtc)
    {
        Quantity = quantity;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Vendue : `InventoryItem` a décrémenté `OnHand` d'autant.</summary>
    internal void Confirm(DateTime nowUtc)
    {
        Status = ReservationStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
    }

    /// <summary>Rendue à la vente sur décision (annulation, paiement refusé).</summary>
    internal void Release(DateTime nowUtc)
    {
        Status = ReservationStatus.Released;
        ReleasedAtUtc = nowUtc;
    }

    /// <summary>Rendue à la vente par le balayage : le panier a été abandonné.</summary>
    internal void Expire(DateTime nowUtc)
    {
        Status = ReservationStatus.Expired;
        ExpiredAtUtc = nowUtc;
    }
}
