using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>CYCLE DE VIE D'UNE FICHE PRODUIT (§5 du cahier Catalog).</summary>
public enum ProductStatus
{
    /// <summary>Brouillon modifiable par le vendeur.</summary>
    Draft = 0,

    /// <summary>Soumis et VERROUILLÉ pour validation.</summary>
    PendingReview = 1,

    /// <summary>Validé par un administrateur, pas encore publié par le vendeur.</summary>
    Approved = 2,

    /// <summary>Refusé ; le vendeur doit corriger.</summary>
    Rejected = 3,

    /// <summary>Visible dans la marketplace.</summary>
    Published = 4,

    /// <summary>Retiré VOLONTAIREMENT par le vendeur.</summary>
    Unpublished = 5,

    /// <summary>Bloqué par la plateforme.</summary>
    Suspended = 6,

    /// <summary>Retiré définitivement du cycle courant.</summary>
    Archived = 7
}

/// <summary>Transitions autorisées — LISTE BLANCHE : ce qui n'est pas écrit est refusé.</summary>
public static class ProductStatusTransitions
{
    public static bool IsAllowed(ProductStatus from, ProductStatus to)
        => (from, to) switch
        {
            (ProductStatus.Draft, ProductStatus.PendingReview) => true,
            (ProductStatus.Draft, ProductStatus.Archived) => true,

            // PENDING_REVIEW NE REVIENT PAS À DRAFT, ET C'EST DÉLIBÉRÉ (§5).
            (ProductStatus.PendingReview, ProductStatus.Approved) => true,
            (ProductStatus.PendingReview, ProductStatus.Rejected) => true,

            (ProductStatus.Rejected, ProductStatus.Draft) => true,
            (ProductStatus.Rejected, ProductStatus.Archived) => true,

            // LA SEULE PORTE VERS PUBLISHED PASSE PAR APPROVED OU UNPUBLISHED.
            (ProductStatus.Approved, ProductStatus.Published) => true,
            (ProductStatus.Unpublished, ProductStatus.Published) => true,

            (ProductStatus.Approved, ProductStatus.Suspended) => true,
            (ProductStatus.Approved, ProductStatus.Archived) => true,

            (ProductStatus.Published, ProductStatus.Unpublished) => true,
            (ProductStatus.Published, ProductStatus.Suspended) => true,

            (ProductStatus.Unpublished, ProductStatus.Archived) => true,

            // SUSPENDED NE MÈNE QU'À APPROVED, ET SÛREMENT PAS À ARCHIVED.
            (ProductStatus.Suspended, ProductStatus.Approved) => true,

            // ARCHIVED est terminal : il n'apparaît à gauche d'aucune règle.
            _ => false
        };

    /// <summary>Vrai si la fiche doit apparaître dans l'API publique (§17).</summary>
    public static bool IsPubliclyVisible(ProductStatus status) => status is ProductStatus.Published;

    /// <summary>Vrai si le vendeur peut encore modifier la révision courante en place.</summary>
    public static bool IsSellerEditable(ProductStatus status)
        => status is ProductStatus.Draft or ProductStatus.Rejected;

    public static Error CannotTransition(ProductStatus from, ProductStatus to)
        => Error.Conflict(
            "catalog.product.invalid_status_transition",
            $"Un produit « {from} » ne peut pas passer à « {to} ».");
}
