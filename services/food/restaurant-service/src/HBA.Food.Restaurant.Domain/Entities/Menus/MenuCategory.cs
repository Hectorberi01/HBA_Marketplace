using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

public readonly record struct MenuCategoryId(Guid Value)
{
    public static MenuCategoryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE SECTION DE CARTE : « Entrées », « Plats », « Boissons ».
///
/// CE TYPE S'APPELAIT <c>Menu</c>. Il a été renommé pour rejoindre le
/// vocabulaire du cahier des charges (§5), qui réserve « Menu » à la CARTE et
/// nomme « MenuCategory » la section.
///
/// Le renommage n'est pas cosmétique : tant que « Menu » désignait une section,
/// il n'y avait aucun mot disponible pour ce qui porte les créneaux horaires — et
/// donc aucun endroit évident où les poser. C'est ainsi qu'un modèle aplati se
/// défend tout seul : le nom manquant fait paraître le niveau manquant superflu.
///
/// POURQUOI LES ARTICLES NE SONT PAS DES ENFANTS DE CETTE SECTION.
///
/// Valider le panier d'un client demande de charger UN article avec ses options.
/// S'il était enfant d'une section, il faudrait charger toute la section — soit
/// quarante plats et leurs options — pour en vérifier un seul. Et déplacer un
/// plat d'une section à l'autre deviendrait un transfert entre agrégats, là où
/// c'est un simple changement de rattachement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le restaurant, PORTÉ DIRECTEMENT et non déduit de la carte.
    ///
    /// Redondant en apparence : la carte le connaît déjà. Mais toutes les gardes
    /// de ce module comparent au restaurant de l'appelant, et passer par la carte
    /// forcerait un chargement supplémentaire à chaque vérification — une lecture
    /// de plus par requête, sur le chemin qui autorise les commandes.
    /// </summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>Carte de rattachement. Un simple identifiant : la carte n'est pas le parent.</summary>
    public Guid MenuId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>
    /// Ordre d'affichage. Les entrées avant les desserts : sans cet ordre, la
    /// carte se réaffiche différemment à chaque chargement.
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Section masquée sans être supprimée. Ses articles restent, prêts à revenir.
    /// </summary>
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

    /// <summary>
    /// Déplace la section vers une autre carte.
    ///
    /// LES ARTICLES SUIVENT SANS BOUGER : ils référencent la SECTION, pas la
    /// carte. Déplacer « Boissons » du menu du midi vers la carte du soir emporte
    /// donc ses quinze boissons, sans qu'aucune ligne d'article ne change — et
    /// c'est bien ce qu'on veut, sinon un déménagement de section serait une
    /// réécriture de toute la carte.
    /// </summary>
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

    /// <summary>
    /// Masque la section.
    ///
    /// N'AGIT PAS SUR LES ARTICLES, et c'est délibéré : ils gardent leur propre
    /// disponibilité. C'est la LECTURE de la carte qui doit écarter les articles
    /// d'une section masquée — un plat momentanément épuisé dans une section
    /// masquée ne doit pas « revenir disponible » quand la section reparaît.
    /// </summary>
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

    /// <summary>Les sections d'une carte donnée. Sert la garde de suppression d'une carte garnie.</summary>
    Task<int> CountInMenuAsync(Guid menuId, CancellationToken cancellationToken = default);

    Task AddAsync(MenuCategory category, CancellationToken cancellationToken = default);

    void Remove(MenuCategory category);
}
