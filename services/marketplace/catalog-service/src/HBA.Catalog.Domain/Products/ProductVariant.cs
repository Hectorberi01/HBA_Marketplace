using HBA.Shared.Domain.Primitives;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// Déclinaison d'un produit (couleur, taille). Le prix et le stock vivent
/// ailleurs, référencés par le SKU (cf. dossier, ProductVariant). Entité enfant
/// de l'agrégat Product.
/// </summary>
public sealed class ProductVariant : Entity<Guid>
{
    private ProductVariant()
    {
    }

    internal ProductVariant(
        Guid id,
        Sku sku,
        Dictionary<string, string> variantAttributes,
        string? barcode,
        int weightGrams,
        Dimensions? dimensions)
        : base(id)
    {
        Sku = sku;
        VariantAttributes = variantAttributes;
        Barcode = barcode;
        WeightGrams = weightGrams;
        Dimensions = dimensions;
    }

    public Sku Sku { get; private set; } = default!;

    /// <summary>Attributs dynamiques (couleur, taille…), mappés en jsonb.</summary>
    public Dictionary<string, string> VariantAttributes { get; private set; } = new();

    public string? Barcode { get; private set; }
    public int WeightGrams { get; private set; }

    /// <summary>
    /// La déclinaison est-elle proposable à la vente ?
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CET ÉTAT MANQUAIT, ET SON ABSENCE ÉTAIT UNE CAPACITÉ EN MOINS
    ///    (tâche #230).
    ///
    /// Le `ProductVariant` du monolithe en avait un ; celui de catalog-service est
    /// né sans. `CreateOfferCommand` le notait déjà : sa garde `variante.IsActive`
    /// n'a pas été transposée « parce qu'il n'y avait rien à tester ». Résultat, on
    /// ne pouvait PAS retirer une taille de la vente sans supprimer le produit
    /// entier — donc sans emporter ses photos, ses autres déclinaisons et
    /// l'historique de ses commandes.
    ///
    /// DÉSACTIVER N'EST PAS SUPPRIMER, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// `RemoveVariant` existe et efface la ligne. Une commande passée référence
    /// cette déclinaison par son identifiant : l'effacer laisse un historique qui
    /// pointe vers rien. Désactiver garde la trace et ferme la vente — c'est le même
    /// raisonnement que l'archivage d'une offre.
    ///
    /// VRAI PAR DÉFAUT, y compris pour les lignes déjà en base : la migration
    /// pose `true`. Une valeur par défaut à `false` retirerait de la vente, en
    /// silence, tout le catalogue existant.
    /// </remarks>
    public bool IsActive { get; private set; } = true;

    /// <summary>Retire la déclinaison de la vente. Idempotent.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>La remet en vente. NE RÉTABLIT AUCUNE OFFRE ARCHIVÉE.</summary>
    /// <remarks>
    /// L'archivage d'une offre est TERMINAL (`OfferStatus.Archived`) : le vendeur
    /// devra recréer sa mise en vente, avec le prix du jour. Réactiver
    /// automatiquement remettrait en vitrine un prix décidé il y a six mois.
    /// </remarks>
    public void Reactivate() => IsActive = true;
    public Dimensions? Dimensions { get; private set; }

    internal void Update(Sku sku, Dictionary<string, string> variantAttributes, string? barcode, int weightGrams)
    {
        Sku = sku;
        VariantAttributes = variantAttributes;
        Barcode = barcode;
        WeightGrams = weightGrams;
    }
}
