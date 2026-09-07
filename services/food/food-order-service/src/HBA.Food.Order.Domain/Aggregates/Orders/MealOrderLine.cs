using HBA.Shared.Domain.Primitives;

namespace HBA.FoodOrders.Domain.Orders;

/// <summary>Une option de plat à figer dans la commande.</summary>
public sealed record MealOrderLineOptionDraft(Guid OptionGroupId, Guid OptionId);

/// <summary>Données d'une ligne à figer au paiement.</summary>
public sealed record MealOrderLineDraft(
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitBasePrice,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    string? Notes = null,
    IReadOnlyList<MealOrderLineOptionDraft>? Options = null);

/// <summary>
/// Ligne de commande : un instantané FIGÉ du plat et de son prix au moment du
/// paiement.
/// </summary>
public sealed class MealOrderLine : Entity<Guid>
{
    private readonly List<MealOrderLineOption> _options = new();

    private MealOrderLine()
    {
    }

    internal MealOrderLine(Guid id, MealOrderLineDraft draft)
        : base(id)
    {
        MenuItemId = draft.MenuItemId;
        Name = draft.Name;
        Notes = draft.Notes;
        Quantity = draft.Quantity;
        UnitBasePrice = draft.UnitBasePrice;
        SellerDiscount = draft.SellerDiscount;
        PlatformDiscount = draft.PlatformDiscount;
        FinalUnitPrice = draft.FinalUnitPrice;

        foreach (var option in draft.Options ?? [])
        {
            _options.Add(new MealOrderLineOption(Guid.NewGuid(), option.OptionGroupId, option.OptionId));
        }
    }

    /// <summary>Le plat dans la carte du restaurant.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>Le nom du plat au moment de l'achat.</summary>
    public string Name { get; private set; } = default!;

    /// <summary>« Sans piment ». Destiné à la cuisine, figé avec la commande.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Les options DEMANDÉES. Libellés et suppléments appartiennent à la cuisine.
    /// </summary>
    public IReadOnlyCollection<MealOrderLineOption> Options => _options.AsReadOnly();

    public int Quantity { get; private set; }

    public decimal UnitBasePrice { get; private set; }

    public decimal SellerDiscount { get; private set; }

    public decimal PlatformDiscount { get; private set; }

    public decimal FinalUnitPrice { get; private set; }

    /// <summary>Total payé pour la ligne (prix final unitaire × quantité).</summary>
    public decimal LineTotal => FinalUnitPrice * Quantity;
}
