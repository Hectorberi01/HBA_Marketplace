namespace HBA.Food.Domain.Staff;

/// <summary>LES QUATRE RÔLES DU CAHIER DES CHARGES (§2).</summary>
public enum StaffRole
{
    /// <summary>Gestion complète : personnel, menus, paramètres, horaires, statistiques.</summary>
    Owner = 0,

    /// <summary>
    /// Pilotage opérationnel : commandes, disponibilités, cuisine, staff partiel.
    /// </summary>
    Manager = 1,

    /// <summary>Réception des commandes : acceptation, refus, suivi opérationnel.</summary>
    Cashier = 2,

    /// <summary>Cuisine seule : tickets, démarrage de préparation, passage en prêt.</summary>
    KitchenStaff = 3
}

/// <summary>LES SEPT PERMISSIONS DU CAHIER DES CHARGES (§2).</summary>
public enum FoodPermission
{
    /// <summary><c>restaurant.order.accept</c></summary>
    OrderAccept = 0,

    /// <summary><c>restaurant.order.reject</c></summary>
    OrderReject = 1,

    /// <summary><c>restaurant.menu.manage</c> — carte, articles, options, disponibilités.</summary>
    MenuManage = 2,

    /// <summary><c>restaurant.staff.manage</c> — toujours borné par la hiérarchie.</summary>
    StaffManage = 3,

    /// <summary><c>restaurant.kitchen.manage</c> — tickets, préparation, prêt.</summary>
    KitchenManage = 4,

    /// <summary>
    /// <c> restaurant.settings.manage</c> — identité, horaires, minimum de
    /// commande, mode d'acceptation.
    /// </summary>
    SettingsManage = 5,

    /// <summary>
    /// <c> restaurant.analytics.read</c> — chiffre d'affaires, panier moyen, taux
    /// de refus.
    /// </summary>
    AnalyticsRead = 6
}

/// <summary>
/// Les permissions par défaut de chaque rôle, et la traduction vers les codes du
/// cahier des charges.
/// </summary>
public static class FoodPermissions
{
    /// <summary>
    /// Le code pointé du cahier des charges (§2), pour les journaux et les jetons.
    /// </summary>
    public static string ToCode(this FoodPermission permission) => permission switch
    {
        FoodPermission.OrderAccept => "restaurant.order.accept",
        FoodPermission.OrderReject => "restaurant.order.reject",
        FoodPermission.MenuManage => "restaurant.menu.manage",
        FoodPermission.StaffManage => "restaurant.staff.manage",
        FoodPermission.KitchenManage => "restaurant.kitchen.manage",
        FoodPermission.SettingsManage => "restaurant.settings.manage",
        FoodPermission.AnalyticsRead => "restaurant.analytics.read",
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
    };

    /// <summary>CE QUE CHAQUE RÔLE PEUT FAIRE, SANS DÉROGATION.</summary>
    public static IReadOnlySet<FoodPermission> DefaultsFor(StaffRole role) => role switch
    {
        StaffRole.Owner => OwnerDefaults,
        StaffRole.Manager => ManagerDefaults,
        StaffRole.Cashier => CashierDefaults,
        StaffRole.KitchenStaff => KitchenDefaults,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static readonly HashSet<FoodPermission> OwnerDefaults = new(Enum.GetValues<FoodPermission>());

    private static readonly HashSet<FoodPermission> ManagerDefaults = new()
    {
        FoodPermission.OrderAccept,
        FoodPermission.OrderReject,
        FoodPermission.MenuManage,
        FoodPermission.StaffManage,
        FoodPermission.KitchenManage,
        FoodPermission.AnalyticsRead
    };

    private static readonly HashSet<FoodPermission> CashierDefaults = new()
    {
        FoodPermission.OrderAccept,
        FoodPermission.OrderReject
    };

    private static readonly HashSet<FoodPermission> KitchenDefaults = new()
    {
        FoodPermission.KitchenManage
    };
}
