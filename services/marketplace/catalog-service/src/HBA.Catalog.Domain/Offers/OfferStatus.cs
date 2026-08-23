using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Offers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CYCLE DE VIE D'UNE OFFRE — INDÉPENDANT DE CELUI DU PRODUIT.
///
/// Un produit publié peut porter une offre active, une en pause et une en
/// rupture : ce sont trois vendeurs différents, ou trois variantes.
///
/// Le module Offers n'en connaissait que trois — Active, Paused, OutOfStock — et
/// il manquait les deux extrémités :
///
///   • DRAFT : une offre naissait ACTIVE. Un vendeur qui préparait son prix le
///     publiait par le seul fait de l'enregistrer.
///   • ARCHIVED : la suppression d'une offre était une VRAIE suppression de
///     ligne. L'historique des prix partait avec, et une commande passée
///     référençait une offre qui n'existait plus.
///
/// SUSPENDED est le pendant de celui du produit : la plateforme retire une offre
/// (prix aberrant, signalement) sans que le vendeur puisse la remettre lui-même.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum OfferStatus
{
    /// <summary>Le vendeur prépare son offre. Invisible.</summary>
    Draft = 0,

    /// <summary>En vente.</summary>
    Active = 1,

    /// <summary>Retirée par le vendeur, réversible d'un geste.</summary>
    Paused = 2,

    /// <summary>Plus de stock. Posé par Inventory, pas à la main.</summary>
    OutOfStock = 3,

    /// <summary>Retirée par la plateforme. Le vendeur ne peut pas la relancer.</summary>
    Suspended = 4,

    /// <summary>Retirée définitivement. La ligne survit pour l'historique.</summary>
    Archived = 5
}

/// <summary>
/// Transitions autorisées. Liste blanche : ce qui n'est pas écrit est refusé.
/// </summary>
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
            //
            // OutOfStock est posé par Inventory quand le stock tombe à zéro, et
            // levé quand il remonte. Un vendeur qui pourrait repasser son offre
            // en Active à la main vendrait ce qu'il n'a pas — et c'est l'acheteur
            // qui l'apprendrait, trois jours plus tard.
            (OfferStatus.OutOfStock, OfferStatus.Active) => true,
            (OfferStatus.OutOfStock, OfferStatus.Paused) => true,
            (OfferStatus.OutOfStock, OfferStatus.Archived) => true,

            // UNE SANCTION PASSE AVANT L'ÉTAT DU STOCK.
            //
            // Cette transition manquait, et son absence rouvrait la vente. Une
            // offre en rupture ne pouvait pas être suspendue quand son vendeur
            // l'était : elle restait OutOfStock. Puis le stock remontait,
            // ReactivateOffersOnStockReplenishedHandler la repassait en Active —
            // et le vendeur écarté par la plateforme revendait, sans que
            // personne n'ait rien décidé.
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
