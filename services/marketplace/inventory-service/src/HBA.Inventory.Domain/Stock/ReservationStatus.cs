namespace HBA.Inventory.Domain.Stock;

/// <summary>Cycle de vie d'une réservation de stock.</summary>
public enum ReservationStatus
{
    /// <summary>En cours : immobilise du stock.</summary>
    Active = 0,

    /// <summary>Vendue. `OnHand` a été décrémenté d'autant.</summary>
    Confirmed = 1,

    /// <summary>Rendue à la vente sur annulation ou paiement refusé.</summary>
    Released = 2,

    /// <summary>Rendue à la vente par le balayage d'expiration (panier abandonné).</summary>
    Expired = 3
}
