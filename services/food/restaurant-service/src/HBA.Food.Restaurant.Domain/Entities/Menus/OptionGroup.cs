using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

/// <summary>
/// Un choix proposé sur un article : « Petite / Moyenne / Grande », « Piment »,
/// « Accompagnement ».
///
/// LE PRIX EST UN ÉCART, PAS UN MONTANT.
///
/// « Grande » ne coûte pas 2 500 F : elle coûte 500 F DE PLUS que le prix de
/// base. Stocker un montant absolu obligerait à réécrire chaque option à chaque
/// changement de prix du plat — et la première option oubliée ferait payer un
/// supplément au prix d'il y a six mois.
///
/// L'écart peut être NÉGATIF : « sans viande, −300 F » est une remise légitime.
/// Le contrôle qui compte est ailleurs — voir MenuItem.PriceSelection, qui refuse
/// qu'un total tombe sous zéro.
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

    /// <summary>Écart appliqué au prix de base, en francs. Peut être négatif.</summary>
    public decimal PriceDelta { get; private set; }

    /// <summary>
    /// Disponibilité de l'option — plus de poulet, plus de fromage.
    ///
    /// DATÉE, PAS BOOLÉENNE. « Plus de fromage aujourd'hui » revient au service
    /// suivant ; sans échéance, le supplément resterait absent des semaines parce
    /// que personne ne pense à recocher une case. Voir ItemAvailability.
    /// </summary>
    public ItemAvailability Availability { get; private set; } = ItemAvailability.Available();

    internal void Rename(string name, decimal priceDelta)
    {
        Name = name.Trim();
        PriceDelta = priceDelta;
    }

    internal void SetAvailability(ItemAvailability availability) => Availability = availability;
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN GROUPE D'OPTIONS, ET SES RÈGLES DE SÉLECTION.
///
/// C'est ici que se joue la différence entre « choisissez une taille »
/// (obligatoire, exactement une) et « suppléments » (facultatif, autant qu'on
/// veut). Deux nombres suffisent à exprimer les deux, et tous les cas
/// intermédiaires : « choisissez 2 accompagnements parmi 5 ».
///
/// CES RÈGLES DÉCIDENT DE CE QUI PART EN CUISINE.
///
/// Un groupe « taille » qui accepterait zéro choix laisserait une commande sans
/// taille : le cuisinier devrait deviner, et il devinera mal une fois sur trois.
/// Un groupe « sauce » qui accepterait trois choix ferait préparer un plat que le
/// client n'a pas voulu — et qu'il renverra.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Nombre minimum de choix. Zéro = groupe facultatif.
    ///
    /// C'est ce nombre, et lui seul, qui rend un groupe obligatoire : un booléen
    /// « IsRequired » séparé aurait pu contredire le minimum, et il aurait fallu
    /// décider lequel des deux ment.
    /// </summary>
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

    /// <summary>
    /// Ce groupe peut-il être satisfait aujourd'hui ?
    ///
    /// UN GROUPE OBLIGATOIRE DONT LES OPTIONS SONT TOUTES ÉPUISÉES REND
    /// L'ARTICLE INCOMMANDABLE.
    ///
    /// Sans ce contrôle, un plat resterait affiché « disponible » alors qu'aucune
    /// taille n'est servable : le client choisirait, verrait son panier refusé, et
    /// ne comprendrait pas — l'écran lui disait que le plat était là.
    /// </summary>
    internal bool CanBeSatisfiedAt(DateTime nowUtc)
        => !IsRequired || _options.Count(o => o.Availability.IsAvailableAt(nowUtc)) >= MinSelections;
}
