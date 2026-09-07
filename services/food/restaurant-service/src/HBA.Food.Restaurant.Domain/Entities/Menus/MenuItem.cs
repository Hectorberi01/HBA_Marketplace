using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

public readonly record struct MenuItemId(Guid Value)
{
    public static MenuItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Ce qu'une sélection d'options coûte, et ce qu'elle contient.</summary>
public sealed record PricedSelection(
    decimal UnitPrice,
    string Currency,
    IReadOnlyList<SelectedOption> Options);

/// <summary>Une option retenue, figée telle qu'elle était au moment du choix.</summary>
public sealed record SelectedOption(Guid OptionId, string GroupName, string OptionName, decimal PriceDelta);

/// <summary>UN ARTICLE DE LA CARTE.</summary>
public sealed class MenuItem : AggregateRoot<MenuItemId>
{
    private readonly List<OptionGroup> _optionGroups = new();

    private MenuItem()
    {
    }

    private MenuItem(MenuItemId id, Guid restaurantId, Guid menuCategoryId, string name, Money basePrice)
        : base(id)
    {
        RestaurantId = restaurantId;
        MenuCategoryId = menuCategoryId;
        Name = name;
        BasePrice = basePrice;
        Availability = ItemAvailability.Available();
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid RestaurantId { get; private set; }

    /// <summary>
    /// Section de rattachement. Un simple identifiant : la section n'est pas le
    /// parent.
    /// </summary>
    public Guid MenuCategoryId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    /// <summary>La photo du plat, PAR RÉFÉRENCE au service média (§6).</summary>
    public Guid? ImageMediaId { get; private set; }

    /// <summary>TRANSITOIRE : l'URL d'avant la bascule.</summary>
    public string? LegacyImageUrl { get; private set; }

    /// <summary>
    /// L'adresse publique de <see cref="ImageMediaId"/> , recopiée au rattachement.
    /// </summary>
    public string? ImagePublicUrl { get; private set; }

    /// <summary>Prix sans option. Les options s'y ajoutent en écart.</summary>
    public Money BasePrice { get; private set; } = default!;

    /// <summary>Disponibilité de l'article.</summary>
    public ItemAvailability Availability { get; private set; } = ItemAvailability.Available();

    public int DisplayOrder { get; private set; }

    /// <summary>Temps de préparation propre à ce plat, en minutes (cahier §6).</summary>
    public int? PreparationMinutes { get; private set; }

    /// <summary>
    /// Poste de préparation (§9) : GRILL, PIZZA, DRINKS. Nul = aucun poste
    /// particulier.
    /// </summary>
    public Guid? PreparationStationId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<OptionGroup> OptionGroups => _optionGroups.AsReadOnly();

    public static Result<MenuItem> Create(
        Guid restaurantId, Guid menuCategoryId, string name, decimal basePrice, string currency = "XOF")
    {
        if (restaurantId == Guid.Empty || menuCategoryId == Guid.Empty)
        {
            return Error.Validation("food.item.parent_required", "L'article doit appartenir à un restaurant et à une section.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.item.name_required", "Le nom de l'article est obligatoire.");
        }

        if (basePrice < 0m)
        {
            return Error.Validation("food.item.price_invalid", "Le prix ne peut pas être négatif.");
        }

        var prix = Money.Create(basePrice, currency);
        if (prix.IsFailure)
        {
            return prix.Error;
        }

        return new MenuItem(MenuItemId.New(), restaurantId, menuCategoryId, name.Trim(), prix.Value);
    }

    /// <param name="displayOrder">
    /// Le rang d'affichage, ou <c> null</c> pour NE PAS Y TOUCHER.
    /// </param>
    public Result UpdateDetails(string name, string? description, int? displayOrder = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("food.item.name_required", "Le nom de l'article est obligatoire."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (displayOrder is { } rang)
        {
            DisplayOrder = rang;
        }

        Touch();

        return Result.Success();
    }

    public Result ChangePrice(decimal basePrice)
    {
        if (basePrice < 0m)
        {
            return Result.Failure(Error.Validation("food.item.price_invalid", "Le prix ne peut pas être négatif."));
        }

        var prix = Money.Create(basePrice, BasePrice.Currency);
        if (prix.IsFailure)
        {
            return Result.Failure(prix.Error);
        }

        BasePrice = prix.Value;
        Touch();
        return Result.Success();
    }

    /// <summary>Rattache la photo du plat (§6).</summary>
    /// <param name="imagePublicUrl">
    /// L'adresse publique du média, telle que le service média l'a rendue au dépôt.
    /// </param>
    public Result SetImage(Guid? imageMediaId, string? imagePublicUrl)
    {
        ImageMediaId = imageMediaId == Guid.Empty ? null : imageMediaId;

        if (ImageMediaId is null)
        {
            // Retirer la photo retire AUSSI son adresse.
            ImagePublicUrl = null;
        }
        else
        {
            LegacyImageUrl = null;
            ImagePublicUrl = string.IsNullOrWhiteSpace(imagePublicUrl)
                ? null
                : imagePublicUrl.Trim();
        }

        Touch();
        return Result.Success();
    }

    /// <summary>Fixe le temps et le poste de préparation (§6, §9).</summary>
    public Result SetPreparation(int? minutes, Guid? preparationStationId)
    {
        if (minutes is { } valeur && valeur is < MinPreparationMinutes or > MaxPreparationMinutes)
        {
            return Result.Failure(Error.Validation(
                "food.item.preparation_invalid",
                $"Le temps de préparation doit être compris entre {MinPreparationMinutes} et {MaxPreparationMinutes} minutes."));
        }

        PreparationMinutes = minutes;
        PreparationStationId = preparationStationId == Guid.Empty ? null : preparationStationId;
        Touch();

        return Result.Success();
    }

    /// <summary>
    /// Une minute pour une boisson, trois heures au grand maximum pour un plat
    /// mijoté.
    /// </summary>
    public const int MinPreparationMinutes = 1;
    public const int MaxPreparationMinutes = 180;

    /// <summary>Déplace l'article vers une autre SECTION.</summary>
    public Result MoveToCategory(Guid menuCategoryId)
    {
        if (menuCategoryId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("food.item.parent_required", "Section de destination requise."));
        }

        MenuCategoryId = menuCategoryId;
        Touch();
        return Result.Success();
    }

    /// <summary>Le plat est épuisé JUSQU'À une échéance, et revient tout seul.</summary>
    public Result MarkUnavailableUntil(DateTime untilUtc, DateTime nowUtc)
    {
        var etat = ItemAvailability.UntilUtc(untilUtc, nowUtc);
        if (etat.IsFailure)
        {
            return Result.Failure(etat.Error);
        }

        Availability = etat.Value;
        Touch();
        return Result.Success();
    }

    /// <summary>Le plat est retiré de la carte jusqu'à nouvel ordre.</summary>
    public Result MarkUnavailableIndefinitely()
    {
        Availability = ItemAvailability.Indefinitely();
        Touch();
        return Result.Success();
    }

    /// <summary>Le plat revient à la carte.</summary>
    public Result MarkAvailable()
    {
        Availability = ItemAvailability.Available();
        Touch();
        return Result.Success();
    }

    // ── Groupes d'options ───────────────────────────────────────────────────

    public Result<Guid> AddOptionGroup(string name, int minSelections, int maxSelections, int displayOrder = 0)
    {
        var groupe = OptionGroup.Create(name, minSelections, maxSelections, displayOrder);
        if (groupe.IsFailure)
        {
            return groupe.Error;
        }

        _optionGroups.Add(groupe.Value);
        Touch();
        return groupe.Value.Id;
    }

    public Result RemoveOptionGroup(Guid groupId)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Result.Failure(Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable."));
        }

        _optionGroups.Remove(groupe);
        Touch();
        return Result.Success();
    }

    public Result<Guid> AddOption(Guid groupId, string name, decimal priceDelta)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable.");
        }

        var option = groupe.AddOption(name, priceDelta);
        if (option.IsFailure)
        {
            return option.Error;
        }

        Touch();
        return option.Value.Id;
    }

    public Result RemoveOption(Guid groupId, Guid optionId)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Result.Failure(Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable."));
        }

        var result = groupe.RemoveOption(optionId);
        if (result.IsSuccess)
        {
            Touch();
        }

        return result;
    }

    public Result SetOptionAvailability(Guid groupId, Guid optionId, ItemAvailability availability)
    {
        var groupe = _optionGroups.FirstOrDefault(g => g.Id == groupId);
        if (groupe is null)
        {
            return Result.Failure(Error.NotFound("food.option_group.not_found", "Groupe d'options introuvable."));
        }

        var result = groupe.SetOptionAvailability(optionId, availability);
        if (result.IsSuccess)
        {
            Touch();
        }

        return result;
    }

    /// <summary>Cet article est-il commandable aujourd'hui ?</summary>
    public bool IsOrderableAt(DateTime nowUtc)
        => HasImage
        && Availability.IsAvailableAt(nowUtc)
        && _optionGroups.All(g => g.CanBeSatisfiedAt(nowUtc));

    /// <summary>L'article porte-t-il une photo ?</summary>
    public bool HasImage => ImageMediaId is not null
        || !string.IsNullOrWhiteSpace(LegacyImageUrl);

    /// <summary>VALIDE UNE SÉLECTION ET EN CALCULE LE PRIX.</summary>
    public Result<PricedSelection> PriceSelection(IReadOnlyCollection<Guid> selectedOptionIds, DateTime nowUtc)
    {
        if (!Availability.IsAvailableAt(nowUtc))
        {
            return Error.Conflict("food.item.unavailable", $"« {Name} » n'est pas disponible aujourd'hui.");
        }

        var erreurs = new List<string>();
        var retenues = new List<SelectedOption>();
        var reconnues = new HashSet<Guid>();

        foreach (var groupe in _optionGroups.OrderBy(g => g.DisplayOrder))
        {
            var choisies = groupe.Options.Where(o => selectedOptionIds.Contains(o.Id)).ToList();

            foreach (var option in choisies)
            {
                reconnues.Add(option.Id);
            }

            if (choisies.Count < groupe.MinSelections)
            {
                erreurs.Add(groupe.MinSelections == 1
                    ? $"« {groupe.Name} » : un choix est obligatoire."
                    : $"« {groupe.Name} » : au moins {groupe.MinSelections} choix sont obligatoires.");
                continue;
            }

            if (choisies.Count > groupe.MaxSelections)
            {
                erreurs.Add(groupe.MaxSelections == 1
                    ? $"« {groupe.Name} » : un seul choix est possible."
                    : $"« {groupe.Name} » : {groupe.MaxSelections} choix au maximum.");
                continue;
            }

            var epuisees = choisies.Where(o => !o.Availability.IsAvailableAt(nowUtc)).ToList();
            if (epuisees.Count > 0)
            {
                erreurs.Add($"« {groupe.Name} » : {string.Join(", ", epuisees.Select(o => o.Name))} — plus disponible aujourd'hui.");
                continue;
            }

            retenues.AddRange(choisies.Select(o => new SelectedOption(o.Id, groupe.Name, o.Name, o.PriceDelta)));
        }

        // LES IDENTIFIANTS INCONNUS SONT UNE ERREUR, PAS UN SILENCE.
        var inconnues = selectedOptionIds.Where(id => !reconnues.Contains(id)).ToList();
        if (inconnues.Count > 0)
        {
            erreurs.Add("Certaines options choisies n'existent plus sur cet article. Rechargez la carte.");
        }

        if (erreurs.Count > 0)
        {
            return Error.Validation("food.item.selection_invalid", string.Join(" ", erreurs));
        }

        var total = BasePrice.Amount + retenues.Sum(o => o.PriceDelta);

        if (total < 0m)
        {
            // Un cumul de remises ne rend pas un plat gratuit ni payant pour le
            // restaurant.
            return Error.Conflict(
                "food.item.price_negative",
                $"Le prix de « {Name} » avec ces options tomberait sous zéro. Contactez le restaurant.");
        }

        return new PricedSelection(total, BasePrice.Currency, retenues);
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux articles de la carte.</summary>
public interface IMenuItemRepository
{
    Task<MenuItem?> GetByIdAsync(MenuItemId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuItem>> ListByRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuItem>> ListByCategoryAsync(
        Guid menuCategoryId, CancellationToken cancellationToken = default);

    /// <summary>Combien d'articles cette section contient-elle ENCORE ?</summary>
    Task<int> CountInCategoryAsync(Guid menuCategoryId, CancellationToken cancellationToken = default);

    Task AddAsync(MenuItem item, CancellationToken cancellationToken = default);

    void Remove(MenuItem item);
}
