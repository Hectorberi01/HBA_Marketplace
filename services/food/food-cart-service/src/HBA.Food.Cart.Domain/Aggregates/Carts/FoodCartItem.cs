using HBA.Shared.Domain.Primitives;

namespace HBA.FoodCarts.Domain.Carts;

/// <summary>
/// Une ligne de panier : un plat, ses options, sa note, et la quantité.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE SEULE NATURE DE LIGNE, ET C'EST TOUT L'INTÉRÊT DE LA SÉPARATION.
///
/// L'ancienne `CartItem` de cart-service portait DEUX natures dans une entité :
/// six champs de marchandise — offre, produit, catégorie, vendeur, SKU, lieu
/// d'expédition — et quatre de restauration, avec un discriminant `Kind` pour
/// dire lesquels lire. Chaque ligne de repas stockait donc six colonnes vides et
/// un SKU vide « pour que la colonne garde sa contrainte ».
///
/// Le coût réel n'était pas l'espace : c'était que chaque prédicat du domaine
/// devait se souvenir de filtrer sur la nature. `AddItem`, `UpdateItemQuantity`,
/// `RemoveItem` portaient tous les trois `i.Kind == Goods`, et l'index unique de
/// la base avait dû devenir un index FILTRÉ parce que toutes les lignes de repas
/// partageaient `OfferId = Guid.Empty`. Un oubli de filtre passait les tests et
/// fusionnait un plat avec un article.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FoodCartItem : Entity<Guid>
{
    private readonly List<FoodCartItemOption> _options = new();

    private FoodCartItem()
    {
    }

    internal FoodCartItem(
        Guid id,
        Guid menuItemId,
        string nameSnapshot,
        decimal unitBaseAmount,
        string currency,
        int quantity,
        string? notes,
        IEnumerable<(Guid GroupId, Guid OptionId)> options)
        : base(id)
    {
        MenuItemId = menuItemId;
        NameSnapshot = nameSnapshot;
        UnitBaseAmount = unitBaseAmount;
        Currency = currency;
        Quantity = quantity;
        Notes = notes;

        foreach (var (groupId, optionId) in options)
        {
            _options.Add(new FoodCartItemOption(Guid.NewGuid(), groupId, optionId));
        }
    }

    /// <summary>Le plat dans la carte du restaurant.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>
    /// Le nom du plat au moment de l'ajout.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// NOUVEAU, ET IL RÉPARE UN PANIER QUI NE SAVAIT PAS S'AFFICHER SEUL.
    ///
    /// L'ancien panier ne portait aucun nom : l'application devait recharger la
    /// carte du restaurant pour savoir écrire « Riz au gras ». Un plat retiré de
    /// la carte entre l'ajout et l'affichage rendait la ligne muette — un prix,
    /// une quantité, et rien pour dire de quoi il s'agit.
    ///
    /// C'est un instantané D'AFFICHAGE, au même titre que
    /// <see cref="UnitBaseAmount"/> : il ne fait pas foi, la carte reste la
    /// source. Mais un panier doit pouvoir se rendre sans dépendre d'une seconde
    /// lecture qui peut échouer.
    ///
    /// Le prix, lui, garde son autorité DANS la carte — voir
    /// <see cref="FoodCartItemOption"/>. La différence tient à ce qu'un nom
    /// périmé se corrige à l'œil et qu'un prix périmé se facture.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string NameSnapshot { get; private set; } = default!;

    /// <summary>« Sans piment », « bien cuit ». Lu par la cuisine, pas par la caisse.</summary>
    public string? Notes { get; private set; }

    public IReadOnlyCollection<FoodCartItemOption> Options => _options.AsReadOnly();

    /// <summary>
    /// Le prix unitaire, suppléments compris, tel que la carte le donnait à
    /// l'instant de l'ajout.
    ///
    /// ESTIMATION D'AFFICHAGE — la commande le recalcule depuis la carte.
    /// </summary>
    public decimal UnitBaseAmount { get; private set; }

    public string Currency { get; private set; } = default!;

    public int Quantity { get; private set; }

    internal void IncreaseQuantity(int by) => Quantity += by;

    internal void SetQuantity(int quantity) => Quantity = quantity;

    /// <summary>
    /// Le prix a-t-il changé dans la carte depuis l'ajout ? Réaligne la ligne.
    ///
    /// APPELÉ QUAND ON RAJOUTE LE MÊME PLAT, PAS À LA LECTURE.
    ///
    /// Deux ajouts successifs du même plat à une heure d'intervalle, avec un
    /// changement de prix entre les deux, produiraient une ligne de quantité 2
    /// dont le prix unitaire serait celui du premier clic. On retient le plus
    /// récent : c'est celui que le client vient de voir à l'écran.
    /// </summary>
    internal void RefreshUnitPrice(decimal unitBaseAmount, string nameSnapshot)
    {
        UnitBaseAmount = unitBaseAmount;
        NameSnapshot = nameSnapshot;
    }

    /// <summary>
    /// Deux lignes sont LA MÊME si elles portent le même plat ET exactement les
    /// mêmes options.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// SANS CETTE COMPARAISON, LE REGROUPEMENT SERAIT FAUX DANS LES DEUX SENS.
    ///
    /// Regrouper sur le seul plat fondrait « riz sans piment » et « riz très
    /// piquant » en une ligne de deux — et la cuisine en sortirait deux
    /// identiques. Ne jamais regrouper créerait une ligne par clic sur « + », et
    /// le client verrait son panier s'allonger au lieu de compter.
    ///
    /// La NOTE ne compte pas dans l'identité : deux « riz » aux mêmes options,
    /// l'un « bien cuit », restent le même plat pour la carte. On garde la
    /// première note plutôt que d'empiler des lignes qui ne diffèrent que par un
    /// texte libre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    internal bool Matches(Guid menuItemId, IReadOnlyCollection<Guid> optionIds)
    {
        if (MenuItemId != menuItemId || _options.Count != optionIds.Count)
        {
            return false;
        }

        // Les options sont peu nombreuses (quelques unités) : un tri suffit, et
        // évite d'allouer un ensemble à chaque comparaison de ligne.
        var miennes = _options.Select(o => o.OptionId).OrderBy(o => o).ToList();
        var siennes = optionIds.OrderBy(o => o).ToList();

        return miennes.SequenceEqual(siennes);
    }
}
