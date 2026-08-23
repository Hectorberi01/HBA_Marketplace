using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CYCLE DE VIE D'UNE FICHE PRODUIT (§5 du cahier Catalog).
///
/// CETTE ÉNUMÉRATION EN REMPLACE UNE À TROIS VALEURS, ET LE RENOMMAGE COMPTE.
///
/// L'ancienne était { Draft, Active, Archived }. « Active » devient
/// <see cref="Published"/> — même sens, autre nom — et le renommage n'est pas
/// cosmétique : le statut est persisté EN CHAÎNE (HasConversion&lt;string&gt;),
/// il traverse Kafka dans ProductStatusChangedIntegrationEvent, et il est
/// comparé LITTÉRALEMENT ailleurs dans le dépôt :
///
///     shared/contracts/HBA.Catalog.Contracts.Grpc/ProductsGrpc.cs
///       IsVisible: string.Equals(product.Status, "Active", …)
///
/// Sans la migration qui réécrit les lignes ET sans la correction de cette
/// comparaison, chaque produit déjà en vente serait devenu invisible — sans
/// erreur, sans journal, et sans que rien ne relie la panne à ce fichier.
///
/// TROIS ÉTATS NE VENAIENT DE NULLE PART, ET C'EST TOUT L'OBJET DU §4.
///
/// PendingReview, Approved et Rejected n'existaient pas : un vendeur passait de
/// Draft à Active par un seul appel. La validation administrateur n'était donc
/// pas « à implémenter plus tard » — elle était STRUCTURELLEMENT impossible,
/// aucun état ne pouvant la représenter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum ProductStatus
{
    /// <summary>Brouillon modifiable par le vendeur. Invisible.</summary>
    Draft = 0,

    /// <summary>Soumis et VERROUILLÉ pour validation. Le vendeur ne modifie plus.</summary>
    PendingReview = 1,

    /// <summary>Validé par un administrateur, pas encore publié par le vendeur.</summary>
    Approved = 2,

    /// <summary>Refusé ; le vendeur doit corriger. Les motifs vivent dans ProductReview.</summary>
    Rejected = 3,

    /// <summary>Visible dans la marketplace. Remplace l'ancien « Active ».</summary>
    Published = 4,

    /// <summary>Retiré VOLONTAIREMENT par le vendeur. Réversible sans nouvelle validation.</summary>
    Unpublished = 5,

    /// <summary>Bloqué par la plateforme. Le vendeur ne peut pas le relancer lui-même.</summary>
    Suspended = 6,

    /// <summary>Retiré définitivement du cycle courant. La ligne survit pour l'historique.</summary>
    Archived = 7
}

/// <summary>
/// Transitions autorisées — LISTE BLANCHE : ce qui n'est pas écrit est refusé.
///
/// Même forme que <c>OfferStatusTransitions</c>, et pour la même raison : une
/// liste noire laisse passer tout ce qu'on n'a pas pensé à interdire, et l'on ne
/// s'en aperçoit qu'en lisant l'état impossible en base.
/// </summary>
public static class ProductStatusTransitions
{
    public static bool IsAllowed(ProductStatus from, ProductStatus to)
        => (from, to) switch
        {
            (ProductStatus.Draft, ProductStatus.PendingReview) => true,
            (ProductStatus.Draft, ProductStatus.Archived) => true,

            // PENDING_REVIEW NE REVIENT PAS À DRAFT, ET C'EST DÉLIBÉRÉ (§5).
            //
            // Un vendeur qui pourrait retirer sa soumission modifierait sa fiche
            // pendant que l'administrateur la lit. Celui-ci approuverait alors un
            // contenu qu'il n'a pas vu — la validation deviendrait une signature
            // en blanc. La sortie de cet état appartient à l'administrateur seul.
            (ProductStatus.PendingReview, ProductStatus.Approved) => true,
            (ProductStatus.PendingReview, ProductStatus.Rejected) => true,

            (ProductStatus.Rejected, ProductStatus.Draft) => true,
            (ProductStatus.Rejected, ProductStatus.Archived) => true,

            // LA SEULE PORTE VERS PUBLISHED PASSE PAR APPROVED OU UNPUBLISHED.
            //
            // C'est la règle absolue du §4, et elle tient dans ces deux lignes :
            // aucune autre paire de l'énumération n'aboutit à Published. Ajouter
            // ici (Draft, Published) « le temps de tester » suffirait à ouvrir la
            // marketplace à des fiches que personne n'a lues.
            (ProductStatus.Approved, ProductStatus.Published) => true,
            (ProductStatus.Unpublished, ProductStatus.Published) => true,

            (ProductStatus.Approved, ProductStatus.Suspended) => true,
            (ProductStatus.Approved, ProductStatus.Archived) => true,

            (ProductStatus.Published, ProductStatus.Unpublished) => true,
            (ProductStatus.Published, ProductStatus.Suspended) => true,

            (ProductStatus.Unpublished, ProductStatus.Archived) => true,

            // SUSPENDED NE MÈNE QU'À APPROVED, ET SÛREMENT PAS À ARCHIVED.
            //
            // Le §5 fait revenir une suspension levée à APPROVED, pas à PUBLISHED :
            // c'est le vendeur qui republie, la plateforme ne remet pas en vente à
            // sa place. Et l'archivage est fermé ici parce qu'il serait une porte
            // de sortie — un vendeur suspendu archiverait sa fiche, en recréerait
            // une identique, et la sanction n'aurait duré qu'une minute.
            //
            // Conséquence à connaître : une fiche suspendue est un cul-de-sac tant
            // qu'un administrateur ne l'a pas restaurée. C'est le comportement
            // voulu ; c'est aussi pourquoi la file de restauration doit être vue.
            (ProductStatus.Suspended, ProductStatus.Approved) => true,

            // ARCHIVED est terminal : il n'apparaît à gauche d'aucune règle.
            _ => false
        };

    /// <summary>
    /// Vrai si la fiche doit apparaître dans l'API publique (§17).
    ///
    /// UN SEUL ÉTAT, ET C'EST LE POINT.
    ///
    /// Approved n'est PAS visible : validé ne veut pas dire mis en vente. Confondre
    /// les deux publierait des fiches que le vendeur préparait pour une date précise.
    /// </summary>
    public static bool IsPubliclyVisible(ProductStatus status) => status is ProductStatus.Published;

    /// <summary>
    /// Vrai si le vendeur peut encore modifier la révision courante en place.
    ///
    /// Hors de ces deux états, une modification critique ouvre une NOUVELLE
    /// révision (§6) au lieu d'écraser ce que l'acheteur voit.
    /// </summary>
    public static bool IsSellerEditable(ProductStatus status)
        => status is ProductStatus.Draft or ProductStatus.Rejected;

    public static Error CannotTransition(ProductStatus from, ProductStatus to)
        => Error.Conflict(
            "catalog.product.invalid_status_transition",
            $"Un produit « {from} » ne peut pas passer à « {to} ».");
}
