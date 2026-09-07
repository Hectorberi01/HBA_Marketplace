using HBA.Shared.Domain.Primitives;

namespace HBA.FoodCarts.Domain.Carts;

/// <summary>Une ligne de panier : un plat, ses options, sa note, et la quantité.</summary>
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

    /// <summary>Le nom du plat au moment de l'ajout.</summary>
    public string NameSnapshot { get; private set; } = default!;

    /// <summary>« Sans piment », « bien cuit ».</summary>
    public string? Notes { get; private set; }

    public IReadOnlyCollection<FoodCartItemOption> Options => _options.AsReadOnly();

    /// <summary>
    /// Le prix unitaire, suppléments compris, tel que la carte le donnait à
    /// l'instant de l'ajout.
    /// </summary>
    public decimal UnitBaseAmount { get; private set; }

    public string Currency { get; private set; } = default!;

    public int Quantity { get; private set; }

    internal void IncreaseQuantity(int by) => Quantity += by;

    internal void SetQuantity(int quantity) => Quantity = quantity;

    /// <summary>Le prix a-t-il changé dans la carte depuis l'ajout ? Réaligne la ligne.</summary>
    internal void RefreshUnitPrice(decimal unitBaseAmount, string nameSnapshot)
    {
        UnitBaseAmount = unitBaseAmount;
        NameSnapshot = nameSnapshot;
    }

    /// <summary>
    /// Deux lignes sont LA MÊME si elles portent le même plat ET exactement les
    /// mêmes options.
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
