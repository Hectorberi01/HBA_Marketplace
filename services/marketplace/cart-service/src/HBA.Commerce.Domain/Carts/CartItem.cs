using HBA.Shared.Domain.Primitives;

namespace HBA.Commerce.Domain.Carts;

/// <summary>
/// Ligne de panier : un snapshot des identifiants et du prix de base au moment de
/// l'ajout.
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

    /// <summary>Ligne de restauration : un plat, ses options, sa note.</summary>
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

    /// <summary>Marchandise ou restauration.</summary>
    public CartLineKind Kind { get; private set; }

    // ── Marchandise ─────────────────────────────────────────────────────────
    public Guid OfferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid SellerId { get; private set; }
    public string Sku { get; private set; } = default!;
    public Guid ShipFromLocationId { get; private set; }

    // ── Restauration ────────────────────────────────────────────────────────

    /// <summary>L'établissement qui préparera ce plat.</summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>Le plat dans la carte du restaurant.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>« Sans piment », « bien cuit ».</summary>
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
