using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Offers;

/// <summary>CYCLE DE VIE D'UNE OFFRE — INDÉPENDANT DE CELUI DU PRODUIT.</summary>
public enum OfferStatus
{
    /// <summary>Le vendeur prépare son offre.</summary>
    Draft = 0,

    /// <summary>En vente.</summary>
    Active = 1,

    /// <summary>Retirée par le vendeur, réversible d'un geste.</summary>
    Paused = 2,

    /// <summary>Plus de stock. Posé par Inventory, pas à la main.</summary>
    OutOfStock = 3,

    /// <summary>Retirée par la plateforme.</summary>
    Suspended = 4,

    /// <summary>Retirée définitivement. La ligne survit pour l'historique.</summary>
    Archived = 5
}

/// <summary>Transitions autorisées. Liste blanche : ce qui n'est pas écrit est refusé.</summary>
public static class OfferStatusTransitions
{
    public static bool IsAllowed(OfferStatus from, OfferStatus to)
        => (from, to) switch
        {
            (OfferStatus.Draft, OfferStatus.Active) => true,
            (OfferStatus.Draft, OfferStatus.Archived) => true,

            (OfferStatus.Active, OfferStatus.Paused) => true,
            (OfferStatus.Active, OfferStatus.OutOfStock) => true,
            (OfferStatus.Active, OfferStatus.Suspended) => true,
            (OfferStatus.Active, OfferStatus.Archived) => true,

            (OfferStatus.Paused, OfferStatus.Active) => true,
            (OfferStatus.Paused, OfferStatus.Archived) => true,

            // LE RETOUR DE RUPTURE N'EST PAS UNE DÉCISION DU VENDEUR.
            (OfferStatus.OutOfStock, OfferStatus.Active) => true,
            (OfferStatus.OutOfStock, OfferStatus.Paused) => true,
            (OfferStatus.OutOfStock, OfferStatus.Archived) => true,

            // UNE SANCTION PASSE AVANT L'ÉTAT DU STOCK.
            (OfferStatus.OutOfStock, OfferStatus.Suspended) => true,

            // Même raison : une offre que le vendeur avait mise en pause doit
            // pouvoir être suspendue, sinon il lui suffirait de la réactiver.
            (OfferStatus.Paused, OfferStatus.Suspended) => true,

            // Seule la plateforme lève une suspension.
            (OfferStatus.Suspended, OfferStatus.Active) => true,
            (OfferStatus.Suspended, OfferStatus.Archived) => true,

            // ARCHIVED est terminal : il n'apparaît à gauche d'aucune règle.
            _ => false
        };

    /// <summary>Vrai si l'offre est achetable.</summary>
    public static bool IsPurchasable(OfferStatus status) => status is OfferStatus.Active;

    public static Error CannotTransition(OfferStatus from, OfferStatus to)
        => Error.Conflict(
            "products.offer.invalid_transition",
            $"Une offre « {from} » ne peut pas passer à « {to} ».");
}
