using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

/// <summary>
/// Un choix proposé sur un article : « Petite / Moyenne / Grande », « Piment », «
/// Accompagnement ».
/// </summary>
public sealed class MenuOption : Entity<Guid>
{
    private MenuOption()
    {
    }

    internal MenuOption(Guid id, string name, decimal priceDelta)
        : base(id)
    {
        Name = name;
        PriceDelta = priceDelta;
        Availability = ItemAvailability.Available();
    }

    public string Name { get; private set; } = default!;

    /// <summary>Écart appliqué au prix de base, en francs.</summary>
    public decimal PriceDelta { get; private set; }

    /// <summary>Disponibilité de l'option — plus de poulet, plus de fromage.</summary>
    public ItemAvailability Availability { get; private set; } = ItemAvailability.Available();

    internal void Rename(string name, decimal priceDelta)
    {
        Name = name.Trim();
        PriceDelta = priceDelta;
    }

    internal void SetAvailability(ItemAvailability availability) => Availability = availability;
}

/// <summary>UN GROUPE D'OPTIONS, ET SES RÈGLES DE SÉLECTION.</summary>
public sealed class OptionGroup : Entity<Guid>
{
    private readonly List<MenuOption> _options = new();

    private OptionGroup()
    {
    }

    internal OptionGroup(Guid id, string name, int minSelections, int maxSelections, int displayOrder)
        : base(id)
    {
        Name = name;
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        DisplayOrder = displayOrder;
    }

    public string Name { get; private set; } = default!;

    /// <summary>Nombre minimum de choix. Zéro = groupe facultatif.</summary>
    public int MinSelections { get; private set; }

    /// <summary>Nombre maximum de choix.</summary>
    public int MaxSelections { get; private set; }

    public int DisplayOrder { get; private set; }

    public IReadOnlyCollection<MenuOption> Options => _options.AsReadOnly();

    /// <summary>Le client doit-il obligatoirement choisir dans ce groupe ?</summary>
    public bool IsRequired => MinSelections > 0;

    internal static Result<OptionGroup> Create(string name, int minSelections, int maxSelections, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.option_group.name_required", "Le nom du groupe d'options est obligatoire.");
        }

        if (minSelections < 0)
        {
            return Error.Validation("food.option_group.min_invalid", "Le minimum ne peut pas être négatif.");
        }

        if (maxSelections < 1)
        {
            // Un maximum à zéro décrit un groupe qu'on ne peut pas utiliser : il
            // s'afficherait au client sans qu'aucun choix soit acceptable.
            return Error.Validation(
                "food.option_group.max_invalid", "Le maximum doit valoir au moins 1.");
        }

        if (minSelections > maxSelections)
        {
            return Error.Validation(
                "food.option_group.range_invalid",
                $"« {name} » exige au moins {minSelections} choix mais n'en autorise que {maxSelections}. "
                + "Aucune commande ne pourrait satisfaire ce groupe.");
        }

        return new OptionGroup(Guid.NewGuid(), name.Trim(), minSelections, maxSelections, displayOrder);
    }

    internal Result<MenuOption> AddOption(string name, decimal priceDelta)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.option.name_required", "Le nom de l'option est obligatoire.");
        }

        var option = new MenuOption(Guid.NewGuid(), name.Trim(), priceDelta);
        _options.Add(option);
        return option;
    }

    internal Result RemoveOption(Guid optionId)
    {
        var option = _options.FirstOrDefault(o => o.Id == optionId);
        if (option is null)
        {
            return Result.Failure(Error.NotFound("food.option.not_found", "Option introuvable."));
        }

        _options.Remove(option);
        return Result.Success();
    }

    internal Result SetOptionAvailability(Guid optionId, ItemAvailability availability)
    {
        var option = _options.FirstOrDefault(o => o.Id == optionId);
        if (option is null)
        {
            return Result.Failure(Error.NotFound("food.option.not_found", "Option introuvable."));
        }

        option.SetAvailability(availability);
        return Result.Success();
    }

    /// <summary>Ce groupe peut-il être satisfait aujourd'hui ?</summary>
    internal bool CanBeSatisfiedAt(DateTime nowUtc)
        => !IsRequired || _options.Count(o => o.Availability.IsAvailableAt(nowUtc)) >= MinSelections;
}
