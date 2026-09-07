using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

public readonly record struct MenuId(Guid Value)
{
    public static MenuId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>UNE CARTE : « Menu du midi », « Carte du soir », « Carte d'été ».</summary>
public sealed class Menu : AggregateRoot<MenuId>
{
    private Menu()
    {
    }

    private Menu(MenuId id, Guid restaurantId, string name, MenuServingWindow window, int displayOrder)
        : base(id)
    {
        RestaurantId = restaurantId;
        Name = name;
        Window = window;
        DisplayOrder = displayOrder;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid RestaurantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Période de validité et créneau horaire.</summary>
    public MenuServingWindow Window { get; private set; } = MenuServingWindow.Always;

    /// <summary>
    /// Le midi avant le soir : sans cet ordre, la carte se réaffiche différemment à
    /// chaque chargement.
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Carte rangée sans être supprimée — celle d'été qu'on remise en novembre.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>Cette carte est-elle proposée à cet instant ?</summary>
    public bool IsServedAt(DateTime nowUtc) => IsActive && Window.IsServedAt(nowUtc);

    public static Result<Menu> Create(
        Guid restaurantId, string name, MenuServingWindow? window = null, int displayOrder = 0)
    {
        if (restaurantId == Guid.Empty)
        {
            return Error.Validation("food.menu.restaurant_required", "La carte doit appartenir à un restaurant.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.menu.name_required", "Le nom de la carte est obligatoire.");
        }

        return new Menu(MenuId.New(), restaurantId, name.Trim(), window ?? MenuServingWindow.Always, displayOrder);
    }

    public Result Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("food.menu.name_required", "Le nom de la carte est obligatoire."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
        return Result.Success();
    }

    public Result SetWindow(MenuServingWindow window)
    {
        Window = window;
        Touch();
        return Result.Success();
    }

    public Result Reorder(int displayOrder)
    {
        DisplayOrder = displayOrder;
        Touch();
        return Result.Success();
    }

    public Result Activate()
    {
        IsActive = true;
        Touch();
        return Result.Success();
    }

    /// <summary>Range la carte.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux cartes.</summary>
public interface IMenuRepository
{
    Task<Menu?> GetByIdAsync(MenuId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Menu>> ListByRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task AddAsync(Menu menu, CancellationToken cancellationToken = default);

    /// <summary>
    /// NE TOUCHE PAS AUX SECTIONS, et rien ici ne peut le vérifier : elles sont un
    /// autre agrégat.
    /// </summary>
    void Remove(Menu menu);
}
