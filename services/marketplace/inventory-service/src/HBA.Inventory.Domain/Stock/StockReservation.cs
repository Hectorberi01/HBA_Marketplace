using HBA.Shared.Domain.Primitives;

namespace HBA.Inventory.Domain.Stock;

/// <summary>
/// Réservation temporaire de stock à la commande, libérée si le paiement échoue
/// (cf. dossier, StockReservation). Entité enfant de l'agrégat InventoryItem.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE LIGNE N'EST PLUS JAMAIS SUPPRIMÉE (ISSUE-045).
///
/// Avant, `ReleaseReservation` et `ConfirmReservation` appelaient
/// `_reservations.RemoveAll(...)`. Le stock n'avait donc aucun historique, et une
/// vente confirmée ne se distinguait plus d'une réservation inexistante.
///
/// Désormais chaque transition MARQUE la ligne (voir <see cref="ReservationStatus"/>)
/// et pose son horodatage. Une ligne dit ce qui lui est arrivé et quand.
///
/// CE QUE CE CHOIX NE COUVRE PAS : LA TABLE NE DÉCROÎT PLUS.
///
/// `stock_reservations` grossit maintenant de façon monotone, et les repositories
/// chargent `Include(i => i.Reservations)` en entier pour pouvoir calculer
/// `Reserved`. Sur un article très vendu, la collection finira par peser. Aucune
/// purge n'est écrite ici : effacer de l'historique est une décision d'exploitation
/// (combien de temps garde-t-on la trace d'une vente ?), pas un geste d'agrégat.
/// C'est un manque connu, à traiter par un travail de purge daté, pas par un
/// retour au `RemoveAll`.
///
/// AUCUN HORODATAGE DE CRÉATION, ET C'EST DÉLIBÉRÉ.
///
/// Les lignes déjà en base n'en ont pas et n'en auront jamais : l'appel qui les a
/// produites est terminé. Une colonne `CreatedAtUtc` non nulle aurait dû leur
/// INVENTER une date — `now()` à la migration, ou `ExpiresAtUtc` moins une durée
/// supposée. Un historique qui ment est pire qu'un historique incomplet. Les
/// horodatages posés ici ne concernent donc que des TRANSITIONS réellement
/// observées par ce code.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>Où en est cette réservation. Voir <see cref="ReservationStatus"/>.</summary>
    public ReservationStatus Status { get; private set; }

    /// <summary>Instant de la confirmation (vente). Nul tant qu'elle n'a pas eu lieu.</summary>
    public DateTime? ConfirmedAtUtc { get; private set; }

    /// <summary>Instant de la libération volontaire (annulation, paiement refusé).</summary>
    public DateTime? ReleasedAtUtc { get; private set; }

    /// <summary>Instant où le balayage a constaté l'expiration. Voir ISSUE-031.</summary>
    public DateTime? ExpiredAtUtc { get; private set; }

    /// <summary>Immobilise-t-elle encore du stock ? Seul l'état qui compte pour `Reserved`.</summary>
    public bool IsActive => Status == ReservationStatus.Active;

    /// <summary>Active ET dépassée : le balayage doit la reprendre.</summary>
    public bool IsExpirableAt(DateTime nowUtc) => IsActive && ExpiresAtUtc <= nowUtc;

    /// <summary>
    /// Rejeu de la même commande sur le même article : on POSE la quantité, on
    /// n'en ajoute pas une seconde (ISSUE-075). Voir `InventoryItem.Reserve`, qui
    /// est le seul appelant et qui a déjà vérifié le disponible.
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
