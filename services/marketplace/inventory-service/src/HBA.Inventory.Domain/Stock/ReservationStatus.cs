namespace HBA.Inventory.Domain.Stock;

/// <summary>
/// Cycle de vie d'une réservation de stock.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE STATUT N'EXISTAIT PAS, ET SON ABSENCE EFFAÇAIT L'HISTOIRE DU STOCK
/// (ISSUE-045).
///
/// `StockReservation` ne portait que `OrderId`, `Quantity` et `ExpiresAtUtc`.
/// Libérer ou confirmer une réservation SUPPRIMAIT la ligne
/// (`_reservations.RemoveAll(...)`). Trois situations radicalement différentes
/// devenaient donc indiscernables :
///
///   • une réservation jamais créée ;
///   • une réservation libérée après un paiement refusé ;
///   • une VENTE confirmée, stock physique déjà décrémenté.
///
/// La troisième est la dangereuse. Rien n'empêchait `ReleaseReservation` de
/// « libérer » une commande déjà vendue : la ligne n'existait plus, la
/// suppression ne trouvait rien, et — pire — si elle avait existé, on aurait
/// rendu à la vente un stock déjà retiré et facturé. C'est le danger que l'audit
/// nomme sur `POST /api/inventory/reservations/release` : vendre deux fois.
///
/// ON NE SUPPRIME PLUS AUCUNE LIGNE. ON LES MARQUE.
///
/// Conséquence directe et NON NÉGOCIABLE : `InventoryItem.Reserved` ne compte
/// QUE les <see cref="Active"/>. Une somme naïve sur toutes les lignes ferait
/// disparaître d'un coup tout le stock vendable de la plateforme, puisque
/// l'historique s'accumule et ne décroît jamais.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum ReservationStatus
{
    /// <summary>En cours : immobilise du stock. Le SEUL statut compté par `Reserved`.</summary>
    Active = 0,

    /// <summary>
    /// Vendue. `OnHand` a été décrémenté d'autant. N'immobilise plus rien —
    /// le stock n'est pas « réservé », il est PARTI.
    /// </summary>
    Confirmed = 1,

    /// <summary>Rendue à la vente sur annulation ou paiement refusé.</summary>
    Released = 2,

    /// <summary>
    /// Rendue à la vente par le balayage d'expiration (panier abandonné).
    /// Distincte de <see cref="Released"/> à dessein : c'est le seul moyen de
    /// mesurer combien de stock dormait faute de balayeur (ISSUE-031).
    /// </summary>
    Expired = 3
}
