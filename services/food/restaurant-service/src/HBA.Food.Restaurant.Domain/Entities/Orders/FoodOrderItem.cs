using HBA.Shared.Domain.Primitives;

namespace HBA.Food.Domain.Orders;

/// <summary>Une option retenue, FIGÉE telle qu'elle était au moment de la commande.</summary>
public sealed class FoodOrderItemOption : Entity<Guid>
{
    private FoodOrderItemOption()
    {
    }

    /// <summary>PUBLIC, ALORS QUE LES MUTATIONS DE LIGNE RESTENT INTERNAL.</summary>
    public FoodOrderItemOption(Guid id, Guid optionId, string groupName, string optionName, decimal priceDelta)
        : base(id)
    {
        OptionId = optionId;
        GroupName = groupName;
        OptionName = optionName;
        PriceDelta = priceDelta;
    }

    /// <summary>Référence vers l'option de la carte.</summary>
    public Guid OptionId { get; private set; }

    public string GroupName { get; private set; } = default!;
    public string OptionName { get; private set; } = default!;
    public decimal PriceDelta { get; private set; }
}

/// <summary>UNE LIGNE DE COMMANDE FOOD — ET LA RÈGLE DE SNAPSHOT DU CAHIER (§13).</summary>
public sealed class FoodOrderItem : Entity<Guid>
{
    private readonly List<FoodOrderItemOption> _options = new();

    private FoodOrderItem()
    {
    }

    /// <summary>
    /// PUBLIC, POUR LA MÊME RAISON QUE CELUI DE <see cref="FoodOrderItemOption"/> —
    /// ET LES TRANSITIONS DE CUISINE RESTENT INTERNAL.
    /// </summary>
    public FoodOrderItem(
        Guid id,
        Guid menuItemId,
        string nameSnapshot,
        decimal unitPrice,
        string currency,
        int quantity,
        string? notes,
        Guid? preparationStationId,
        int preparationMinutes,
        IEnumerable<FoodOrderItemOption> options)
        : base(id)
    {
        MenuItemId = menuItemId;
        NameSnapshot = nameSnapshot;
        UnitPrice = unitPrice;
        Currency = currency;
        Quantity = quantity;
        Notes = notes;
        PreparationStationId = preparationStationId;
        PreparationMinutes = preparationMinutes;
        Status = KitchenItemStatus.Pending;
        _options.AddRange(options);
    }

    /// <summary>Trace vers la carte. L'article peut avoir été supprimé depuis.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>Le nom TEL QU'AFFICHÉ au client.</summary>
    public string NameSnapshot { get; private set; } = default!;

    /// <summary>Prix unitaire options COMPRISES, figé au moment de la commande.</summary>
    public decimal UnitPrice { get; private set; }

    public string Currency { get; private set; } = default!;
    public int Quantity { get; private set; }

    /// <summary>« sans oignon », « bien cuit ».</summary>
    public string? Notes { get; private set; }

    /// <summary>Poste de préparation, FIGÉ lui aussi.</summary>
    public Guid? PreparationStationId { get; private set; }

    /// <summary>Temps de préparation retenu pour cette ligne, en minutes.</summary>
    public int PreparationMinutes { get; private set; }

    public KitchenItemStatus Status { get; private set; }

    public IReadOnlyCollection<FoodOrderItemOption> Options => _options.AsReadOnly();

    /// <summary>Ce que la ligne coûte. Multiplié APRÈS le calcul unitaire, jamais avant.</summary>
    public decimal LineTotal => UnitPrice * Quantity;

    internal bool Start()
    {
        if (Status != KitchenItemStatus.Pending)
        {
            return false;
        }

        Status = KitchenItemStatus.Preparing;
        return true;
    }

    /// <summary>Marque la ligne prête.</summary>
    internal bool MarkReady()
    {
        if (Status == KitchenItemStatus.Ready)
        {
            return false;
        }

        Status = KitchenItemStatus.Ready;
        return true;
    }

    /// <summary>La ligne repart en préparation : plat renversé, erreur de saisie.</summary>
    internal bool Reopen()
    {
        if (Status != KitchenItemStatus.Ready)
        {
            return false;
        }

        Status = KitchenItemStatus.Preparing;
        return true;
    }
}
