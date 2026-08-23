using HBA.Shared.Domain.Primitives;

namespace HBA.Commerce.Domain.Carts;

/// <summary>
/// Ligne de panier : un snapshot des identifiants et du prix de base au moment de
/// l'ajout. Le prix effectif (promotions) est calculé à la volée par le module
/// Pricing, jamais stocké ici. Entité enfant de l'agrégat Cart.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX NATURES DE LIGNE DANS UNE SEULE ENTITÉ.
///
/// Une ligne <c>Goods</c> renseigne <see cref="OfferId"/>, <see cref="Sku"/>,
/// <see cref="ShipFromLocationId"/> ; une ligne <c>Food</c> renseigne
/// <see cref="RestaurantId"/>, <see cref="MenuItemId"/> et ses options. Les champs
/// de l'autre nature restent vides.
///
/// Pourquoi pas deux entités : elles partagent la quantité, la devise, le prix
/// instantané, l'appartenance au panier et tout le calcul de totaux. Les séparer
/// obligerait Pricing, le calcul du panier et le checkout à traiter deux
/// collections partout — et à se souvenir de la seconde, ce qui finit par se
/// perdre. Le discriminant est explicite (<see cref="Kind"/>) plutôt que déduit
/// de la nullité d'un champ, qui est une supposition qu'on oublie de vérifier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CartItem : Entity<Guid>
{
    private readonly List<CartItemOption> _options = new();

    private CartItem()
    {
    }

    /// <summary>Ligne de marchandise : une offre du catalogue.</summary>
    internal CartItem(
        Guid id,
        Guid offerId,
        Guid productId,
        Guid categoryId,
        Guid sellerId,
        string sku,
        Guid shipFromLocationId,
        decimal unitBaseAmount,
        string currency,
        int quantity)
        : base(id)
    {
        Kind = CartLineKind.Goods;
        OfferId = offerId;
        ProductId = productId;
        CategoryId = categoryId;
        SellerId = sellerId;
        Sku = sku;
        ShipFromLocationId = shipFromLocationId;
        UnitBaseAmount = unitBaseAmount;
        Currency = currency;
        Quantity = quantity;
    }

    /// <summary>
    /// Ligne de restauration : un plat, ses options, sa note.
    ///
    /// <paramref name="unitBaseAmount"/> EST UNE ESTIMATION D'AFFICHAGE.
    ///
    /// Elle comprend le prix du plat et les suppléments retenus, tels que Food les
    /// donnait à l'instant de l'ajout. Le montant FACTURÉ est recalculé par Food à
    /// la réception de la commande, à partir de sa propre carte : un plat dont le
    /// prix a changé entre l'ajout et le paiement est facturé au prix de la carte,
    /// pas à celui du panier.
    /// </summary>
    internal CartItem(
        Guid id,
        Guid restaurantId,
        Guid menuItemId,
        decimal unitBaseAmount,
        string currency,
        int quantity,
        string? notes,
        IEnumerable<(Guid GroupId, Guid OptionId)> options)
        : base(id)
    {
        Kind = CartLineKind.Food;
        RestaurantId = restaurantId;
        MenuItemId = menuItemId;
        UnitBaseAmount = unitBaseAmount;
        Currency = currency;
        Quantity = quantity;
        Notes = notes;

        // Le SKU reste vide : un plat n'en a pas, et lui en inventer un le ferait
        // chercher dans Inventory par la saga de réservation.
        Sku = string.Empty;

        foreach (var (groupId, optionId) in options)
        {
            _options.Add(new CartItemOption(Guid.NewGuid(), groupId, optionId));
        }
    }

    /// <summary>Marchandise ou restauration. Décide du chemin d'exécution en aval.</summary>
    public CartLineKind Kind { get; private set; }

    // ── Marchandise ─────────────────────────────────────────────────────────
    public Guid OfferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid SellerId { get; private set; }
    public string Sku { get; private set; } = default!;
    public Guid ShipFromLocationId { get; private set; }

    // ── Restauration ────────────────────────────────────────────────────────

    /// <summary>L'établissement qui préparera ce plat. Vide pour une marchandise.</summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>Le plat dans la carte du restaurant. Vide pour une marchandise.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>« Sans piment », « bien cuit ». Lu par la cuisine, pas par la caisse.</summary>
    public string? Notes { get; private set; }

    public IReadOnlyCollection<CartItemOption> Options => _options.AsReadOnly();

    // ── Commun ──────────────────────────────────────────────────────────────
    public decimal UnitBaseAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public int Quantity { get; private set; }

    internal void IncreaseQuantity(int by) => Quantity += by;

    internal void SetQuantity(int quantity) => Quantity = quantity;

    /// <summary>
    /// Deux lignes food sont LA MÊME si elles portent le même plat ET exactement
    /// les mêmes options.
    ///
    /// SANS CETTE COMPARAISON, LE REGROUPEMENT SERAIT FAUX DANS LES DEUX SENS.
    ///
    /// Regrouper sur le seul plat fondrait « riz sans piment » et « riz très
    /// piquant » en une ligne de deux — et la cuisine en sortirait deux
    /// identiques. Ne jamais regrouper créerait une ligne par clic sur « + », et
    /// le client verrait son panier s'allonger au lieu de compter.
    ///
    /// La NOTE ne compte pas dans l'identité : deux « riz » aux mêmes options mais
    /// l'un « bien cuit » restent le même plat pour la carte. On garde la première
    /// note plutôt que d'empiler des lignes qui ne diffèrent que par un texte
    /// libre.
    /// </summary>
    internal bool MatchesFood(Guid menuItemId, IReadOnlyCollection<Guid> optionIds)
    {
        if (Kind != CartLineKind.Food || MenuItemId != menuItemId || _options.Count != optionIds.Count)
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
