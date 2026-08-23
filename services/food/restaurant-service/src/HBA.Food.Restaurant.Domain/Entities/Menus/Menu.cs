using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

public readonly record struct MenuId(Guid Value)
{
    public static MenuId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE CARTE : « Menu du midi », « Carte du soir », « Carte d'été ».
///
/// CE TYPE EST NOUVEAU. CE QUI S'APPELAIT <c>Menu</c> S'APPELLE DÉSORMAIS
/// <see cref="MenuCategory"/>.
///
/// La première version aplatissait les deux niveaux du cahier (§5) en un seul :
/// « Menu » y désignait une section — Entrées, Plats, Boissons. C'était plus
/// simple, et cela rendait impossible la seule chose qui justifie le niveau
/// supplémentaire : SERVIR UNE CARTE DIFFÉRENTE SELON L'HEURE.
///
/// Un maquis qui propose un menu ouvrier à midi et une carte complète le soir
/// devait tout mettre côte à côte, et le client voyait à 12 h des plats qu'on ne
/// prépare qu'à 20 h. La seule parade était de masquer et démasquer des sections
/// à la main, deux fois par jour, tous les jours.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI LA BASCULE MAINTENANT ET PAS PLUS TARD
///
/// Aucune commande ne référence encore un article. La reprise ne déplace donc que
/// des lignes de carte. Une fois le panier et les commandes branchés, la même
/// bascule aurait demandé de démêler des références historiques — et le §20 du
/// cahier interdit de modifier rétroactivement une commande déjà passée.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// LES SECTIONS NE SONT PAS ENFANTS DE CET AGRÉGAT.
///
/// Même raison que pour les articles vis-à-vis des sections : servir la carte
/// publique charge des projections, pas des agrégats, et une carte propriétaire
/// de ses sections obligerait à tout charger pour renommer une catégorie.
/// </summary>
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

    /// <summary>Période de validité et créneau horaire. Voir <see cref="MenuServingWindow"/>.</summary>
    public MenuServingWindow Window { get; private set; } = MenuServingWindow.Always;

    /// <summary>Le midi avant le soir : sans cet ordre, la carte se réaffiche différemment à chaque chargement.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// Carte rangée sans être supprimée — celle d'été qu'on remise en novembre.
    ///
    /// À DISTINGUER DU CRÉNEAU : « inactive » est une décision du restaurateur qui
    /// dure jusqu'à ce qu'il la reprenne ; « hors créneau » se lève tout seul à
    /// 11 h le lendemain. Les fondre obligerait quelqu'un à réactiver une carte
    /// chaque matin.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>
    /// Cette carte est-elle proposée à cet instant ?
    ///
    /// Les DEUX conditions : rangée par le restaurateur, ou hors de son créneau.
    /// Interroger l'une sans l'autre laisserait passer une carte d'été désactivée
    /// mais dont les heures collent.
    /// </summary>
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

    /// <summary>
    /// Range la carte.
    ///
    /// N'AGIT NI SUR SES SECTIONS NI SUR SES ARTICLES, délibérément : ils gardent
    /// leur propre état. C'est la LECTURE qui les écarte — un plat épuisé dans une
    /// carte rangée ne doit pas « revenir disponible » quand la carte reparaît.
    /// </summary>
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
    /// NE TOUCHE PAS AUX SECTIONS, et rien ici ne peut le vérifier : elles sont
    /// un autre agrégat. C'est à l'appelant de refuser la suppression d'une carte
    /// encore garnie — voir <c>DeleteMenuCommand</c>.
    /// </summary>
    void Remove(Menu menu);
}
