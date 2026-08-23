using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Domain.Staff;
using HBA.Food.Domain.Stations;
using Microsoft.EntityFrameworkCore;

namespace HBA.Food.Infrastructure.Persistence;

internal sealed class RestaurantRepository : IRestaurantRepository
{
    private readonly FoodDbContext _dbContext;

    public RestaurantRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF : toutes les lectures de ce dépôt précèdent une mutation. Les
    // écrans passent par les requêtes, qui projettent sans suivre.
    public async Task<Restaurant?> GetByIdAsync(RestaurantId id, CancellationToken cancellationToken = default)
        => await _dbContext.Restaurants.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<Restaurant?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
        => await _dbContext.Restaurants.FirstOrDefaultAsync(r => r.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<IReadOnlyList<Restaurant>> ListByStatusAsync(
        RestaurantStatus status, int take, CancellationToken cancellationToken = default)
        // Les plus anciens d'abord : un dossier qui attend depuis trois jours passe
        // avant celui soumis à l'instant. Sans cet ordre, un dossier difficile est
        // indéfiniment doublé — et son restaurant ne vend pas.
        => await _dbContext.Restaurants
            .AsNoTracking()
            .Where(r => r.Status == status)
            .OrderBy(r => r.UpdatedOnUtc ?? r.CreatedOnUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Restaurant>> ListPubliclyVisibleAsync(
        int skip, int take, CancellationToken cancellationToken = default)
        // `AsNoTracking` : lecture pure, aucune mutation ne suit.
        //
        // Sans lui, EF suivrait chaque établissement d'une page de vitrine — et
        // le suivi coûte à la fois de la mémoire et un balayage de détection de
        // changements au premier `SaveChanges` de la requête, qui n'arrivera
        // jamais.
        //
        // ORDRE STABLE OBLIGATOIRE POUR PAGINER.
        //
        // Sans `OrderBy`, PostgreSQL ne garantit AUCUN ordre entre deux requêtes :
        // la page 2 pourrait redonner des établissements déjà vus page 1, et en
        // omettre d'autres définitivement. Le nom est l'ordre le moins surprenant
        // tant qu'aucun classement métier n'existe ; `Id` départage les homonymes.
        => await _dbContext.Restaurants
            .AsNoTracking()
            .Where(r => r.Status == RestaurantStatus.Active)
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Restaurant restaurant, CancellationToken cancellationToken = default)
        => await _dbContext.Restaurants.AddAsync(restaurant, cancellationToken);
}

internal sealed class MenuRepository : IMenuRepository
{
    private readonly FoodDbContext _dbContext;

    public MenuRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    public async Task<Menu?> GetByIdAsync(MenuId id, CancellationToken cancellationToken = default)
        => await _dbContext.Menus.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Menu>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.Menus
            .Where(m => m.RestaurantId == restaurantId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Menu menu, CancellationToken cancellationToken = default)
        => await _dbContext.Menus.AddAsync(menu, cancellationToken);

    public void Remove(Menu menu) => _dbContext.Menus.Remove(menu);
}

internal sealed class MenuCategoryRepository : IMenuCategoryRepository
{
    private readonly FoodDbContext _dbContext;

    public MenuCategoryRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    public async Task<MenuCategory?> GetByIdAsync(
        MenuCategoryId id, CancellationToken cancellationToken = default)
        => await _dbContext.MenuCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MenuCategory>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.MenuCategories
            .Where(c => c.RestaurantId == restaurantId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(cancellationToken);

    // Compté EN BASE : la garde de suppression d'une carte garnie s'appuie sur ce
    // nombre, et charger les sections pour les compter en mémoire coûterait une
    // matérialisation pour un entier.
    public async Task<int> CountInMenuAsync(Guid menuId, CancellationToken cancellationToken = default)
        => await _dbContext.MenuCategories
            .AsNoTracking()
            .CountAsync(c => c.MenuId == menuId, cancellationToken);

    public async Task AddAsync(MenuCategory category, CancellationToken cancellationToken = default)
        => await _dbContext.MenuCategories.AddAsync(category, cancellationToken);

    public void Remove(MenuCategory category) => _dbContext.MenuCategories.Remove(category);
}

internal sealed class MenuItemRepository : IMenuItemRepository
{
    private readonly FoodDbContext _dbContext;

    public MenuItemRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    // Les groupes d'options sont des types « owned » : EF les charge avec la
    // racine, sans Include. C'est exactement ce qu'on veut — PriceSelection en
    // dépend, et un chargement partiel rendrait sa validation fausse.
    public async Task<MenuItem?> GetByIdAsync(MenuItemId id, CancellationToken cancellationToken = default)
        => await _dbContext.MenuItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MenuItem>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.MenuItems
            .Where(i => i.RestaurantId == restaurantId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MenuItem>> ListByCategoryAsync(
        Guid menuCategoryId, CancellationToken cancellationToken = default)
        => await _dbContext.MenuItems
            .Where(i => i.MenuCategoryId == menuCategoryId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(cancellationToken);

    // AsNoTracking et pas de matérialisation : on compte en base, on ne charge
    // pas quarante plats et leurs options pour savoir s'il en reste.
    public async Task<int> CountInCategoryAsync(
        Guid menuCategoryId, CancellationToken cancellationToken = default)
        => await _dbContext.MenuItems
            .AsNoTracking()
            .CountAsync(i => i.MenuCategoryId == menuCategoryId, cancellationToken);

    public async Task AddAsync(MenuItem item, CancellationToken cancellationToken = default)
        => await _dbContext.MenuItems.AddAsync(item, cancellationToken);

    public void Remove(MenuItem item) => _dbContext.MenuItems.Remove(item);
}

internal sealed class PreparationStationRepository : IPreparationStationRepository
{
    private readonly FoodDbContext _dbContext;

    public PreparationStationRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    public async Task<PreparationStation?> GetByIdAsync(
        PreparationStationId id, CancellationToken cancellationToken = default)
        => await _dbContext.PreparationStations.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PreparationStation>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.PreparationStations
            .AsNoTracking()
            .Where(s => s.RestaurantId == restaurantId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

    public async Task<PreparationStation?> GetByCodeAsync(
        Guid restaurantId, string code, CancellationToken cancellationToken = default)
        => await _dbContext.PreparationStations
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.RestaurantId == restaurantId && s.Code == code, cancellationToken);

    /// <summary>
    /// COMPTE LES ARTICLES DE LA CARTE, PAS LES LIGNES DE COMMANDE.
    ///
    /// Les lignes déjà passées portent un poste FIGÉ : supprimer le poste ne les
    /// abîme pas, elles gardent leur identifiant en trace. Ce qui casserait, ce
    /// sont les ARTICLES qui le désignent encore — leurs futurs tickets
    /// n'apparaîtraient sur aucun écran découpé par poste.
    /// </summary>
    public async Task<int> CountItemsUsingAsync(
        Guid preparationStationId, CancellationToken cancellationToken = default)
        => await _dbContext.MenuItems
            .AsNoTracking()
            .CountAsync(i => i.PreparationStationId == preparationStationId, cancellationToken);

    public async Task AddAsync(PreparationStation station, CancellationToken cancellationToken = default)
        => await _dbContext.PreparationStations.AddAsync(station, cancellationToken);

    public void Remove(PreparationStation station) => _dbContext.PreparationStations.Remove(station);
}

internal sealed class FoodOrderRepository : IFoodOrderRepository
{
    private readonly FoodDbContext _dbContext;

    public FoodOrderRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    // Les lignes et leurs options sont des types « owned » : EF les charge avec
    // la racine, sans Include. C'est indispensable — KitchenStatus se DÉRIVE des
    // lignes, et un chargement partiel le rendrait faux plutôt qu'absent.
    public async Task<FoodOrder?> GetByIdAsync(FoodOrderId id, CancellationToken cancellationToken = default)
        => await _dbContext.FoodOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    // L'origine d'abord : c'est la colonne de tête de `ux_food_orders_order`, et
    // un filtre sur `OrderId` seul ne pourrait pas s'en servir.
    public async Task<FoodOrder?> GetByOrderIdAsync(
        FoodOrderOrigin origin, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.FoodOrders.FirstOrDefaultAsync(
            o => o.Origin == origin && o.OrderId == orderId, cancellationToken);

    /// <summary>
    /// Ce qui est ENCORE EN JEU dans ce restaurant.
    ///
    /// Les états terminaux — refusée, livrée, annulée — sont écartés en base : un
    /// écran de cuisine qui accumulerait le service de la veille deviendrait
    /// illisible en une soirée.
    /// </summary>
    public async Task<IReadOnlyList<FoodOrder>> ListActiveAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.FoodOrders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId
                && o.Status != FoodOrderStatus.Rejected
                && o.Status != FoodOrderStatus.Cancelled
                && o.Status != FoodOrderStatus.Delivered
                && o.Status != FoodOrderStatus.PickedUp)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FoodOrder>> ListByStatusAsync(
        Guid restaurantId, FoodOrderStatus status, int take, CancellationToken cancellationToken = default)
        => await _dbContext.FoodOrders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId && o.Status == status)
            .OrderBy(o => o.ReceivedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Les commandes en cours, pour la saturation (§14 : <c>MaximumActiveOrders</c>).
    ///
    /// « En cours » s'arrête à l'enlèvement : une fois le sac parti, la cuisine est
    /// libre, même si la livraison prendra encore vingt minutes.
    /// </summary>
    public async Task<int> CountActiveAsync(Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.FoodOrders
            .AsNoTracking()
            .CountAsync(
                o => o.RestaurantId == restaurantId
                    && (o.Status == FoodOrderStatus.PendingRestaurantAcceptance
                        || o.Status == FoodOrderStatus.Accepted
                        || o.Status == FoodOrderStatus.Preparing
                        || o.Status == FoodOrderStatus.ReadyForPickup),
                cancellationToken);

    public async Task AddAsync(FoodOrder order, CancellationToken cancellationToken = default)
        => await _dbContext.FoodOrders.AddAsync(order, cancellationToken);
}

internal sealed class RestaurantStaffRepository : IRestaurantStaffRepository
{
    private readonly FoodDbContext _dbContext;

    public RestaurantStaffRepository(FoodDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF SUR TOUTES CES LECTURES, Y COMPRIS CELLES DE L'ACTEUR.
    //
    // L'acteur n'est pas muté, mais il est chargé DANS LA MÊME UNITÉ DE TRAVAIL
    // que sa cible : le lire sans suivi ferait qu'un membre chargé deux fois
    // — acteur d'un geste, cible d'un autre — existerait en deux exemplaires
    // divergents dans le même SaveChanges.
    public async Task<RestaurantStaff?> GetByIdAsync(
        RestaurantStaffId id, CancellationToken cancellationToken = default)
        => await _dbContext.Staff.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<RestaurantStaff?> GetMembershipAsync(
        Guid restaurantId, Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Staff.FirstOrDefaultAsync(
            s => s.RestaurantId == restaurantId && s.UserId == userId, cancellationToken);

    public async Task<RestaurantStaff?> GetActiveMembershipByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive, cancellationToken);

    public async Task<IReadOnlyList<RestaurantStaff>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.Staff
            .AsNoTracking()
            .Where(s => s.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Compté EN BASE, pas en mémoire.
    ///
    /// La garde du dernier propriétaire s'appuie sur ce nombre. Le calculer depuis
    /// une liste chargée le rendrait juste au moment du chargement et faux à celui
    /// de la décision — c'est le verrou optimiste sur la ligne modifiée qui ferme
    /// la fenêtre, mais encore faut-il ne pas l'élargir soi-même.
    /// </summary>
    public async Task<int> CountActiveOwnersAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
        => await _dbContext.Staff
            .AsNoTracking()
            .CountAsync(
                s => s.RestaurantId == restaurantId && s.IsActive && s.Role == StaffRole.Owner,
                cancellationToken);

    public async Task AddAsync(RestaurantStaff staff, CancellationToken cancellationToken = default)
        => await _dbContext.Staff.AddAsync(staff, cancellationToken);
}
