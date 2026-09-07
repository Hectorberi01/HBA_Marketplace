using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

public readonly record struct MenuCategoryId(Guid Value)
{
    public static MenuCategoryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>UNE SECTION DE CARTE : « Entrées », « Plats », « Boissons ».</summary>
public sealed class MenuCategory : AggregateRoot<MenuCategoryId>
{
    private MenuCategory()
    {
    }

    private MenuCategory(MenuCategoryId id, Guid restaurantId, Guid menuId, string name, int displayOrder)
        : base(id)
    {
        RestaurantId = restaurantId;
        MenuId = menuId;
        Name = name;
        DisplayOrder = displayOrder;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>Le restaurant, PORTÉ DIRECTEMENT et non déduit de la carte.</summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>
    /// Carte de rattachement. Un simple identifiant : la carte n'est pas le parent.
    /// </summary>
    public Guid MenuId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>
    /// Ordre d'affichage. Les entrées avant les desserts : sans cet ordre, la carte
    /// se réaffiche différemment à chaque chargement.
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Section masquée sans être supprimée.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public static Result<MenuCategory> Create(Guid restaurantId, Guid menuId, string name, int displayOrder = 0)
    {
        if (restaurantId == Guid.Empty || menuId == Guid.Empty)
        {
            return Error.Validation(
                "food.category.parent_required", "La section doit appartenir à un restaurant et à une carte.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.category.name_required", "Le nom de la section est obligatoire.");
        }

        return new MenuCategory(MenuCategoryId.New(), restaurantId, menuId, name.Trim(), displayOrder);
    }

    public Result Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(
                Error.Validation("food.category.name_required", "Le nom de la section est obligatoire."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
        return Result.Success();
    }

    /// <summary>Déplace la section vers une autre carte.</summary>
    public Result MoveToMenu(Guid menuId)
    {
        if (menuId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("food.category.parent_required", "Carte de destination requise."));
        }

        MenuId = menuId;
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

    /// <summary>Masque la section.</summary>
    public Result Deactivate()
    {
        IsActive = false;
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux sections de carte.</summary>
public interface IMenuCategoryRepository
{
    Task<MenuCategory?> GetByIdAsync(MenuCategoryId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MenuCategory>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);

    /// <summary>Les sections d'une carte donnée.</summary>
    Task<int> CountInMenuAsync(Guid menuId, CancellationToken cancellationToken = default);

    Task AddAsync(MenuCategory category, CancellationToken cancellationToken = default);

    void Remove(MenuCategory category);
}
