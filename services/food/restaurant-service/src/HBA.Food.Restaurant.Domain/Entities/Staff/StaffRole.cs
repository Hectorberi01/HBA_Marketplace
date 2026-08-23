namespace HBA.Food.Domain.Staff;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES QUATRE RÔLES DU CAHIER DES CHARGES (§2).
///
/// L'ORDRE DES VALEURS EST UNE HIÉRARCHIE, PAS UN ORDRE D'APPARITION.
///
/// <c>Owner = 0</c> est le plus haut, <c>KitchenStaff = 3</c> le plus bas. C'est
/// sur cette comparaison que repose toute la protection contre l'escalade de
/// privilèges : un employé ne peut agir que sur quelqu'un de STRICTEMENT PLUS
/// BAS que lui.
///
/// NE JAMAIS RÉORDONNER NI INSÉRER AU MILIEU. Les valeurs sont persistées, et
/// intercaler un rôle décalerait la hiérarchie de tous ceux qui suivent — un
/// cuisinier deviendrait caissier sans qu'aucune ligne de code n'ait changé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum StaffRole
{
    /// <summary>Gestion complète : personnel, menus, paramètres, horaires, statistiques.</summary>
    Owner = 0,

    /// <summary>Pilotage opérationnel : commandes, disponibilités, cuisine, staff partiel.</summary>
    Manager = 1,

    /// <summary>Réception des commandes : acceptation, refus, suivi opérationnel.</summary>
    Cashier = 2,

    /// <summary>Cuisine seule : tickets, démarrage de préparation, passage en prêt.</summary>
    KitchenStaff = 3
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES SEPT PERMISSIONS DU CAHIER DES CHARGES (§2).
///
/// Le cahier les nomme en chaînes pointées — <c>restaurant.order.accept</c> — et
/// elles sont reprises ici en énumération. La chaîne reste disponible via
/// <see cref="FoodPermissions.ToCode"/> : c'est elle qui voyagera dans un jeton
/// ou un journal d'audit, l'énumération sert à ce que le compilateur refuse une
/// faute de frappe.
///
/// POURQUOI DES PERMISSIONS ET PAS SEULEMENT DES RÔLES
///
/// Le cahier l'exige : « les rôles fournissent des permissions par défaut, mais
/// le modèle doit permettre des permissions fines ». Un vrai restaurant a
/// toujours le caissier de confiance qui gère aussi la carte, et le manager à qui
/// l'on ne confie pas les paramètres commerciaux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary><c>restaurant.settings.manage</c> — identité, horaires, minimum de commande, mode d'acceptation.</summary>
    SettingsManage = 5,

    /// <summary><c>restaurant.analytics.read</c> — chiffre d'affaires, panier moyen, taux de refus.</summary>
    AnalyticsRead = 6
}

/// <summary>
/// Les permissions par défaut de chaque rôle, et la traduction vers les codes du
/// cahier des charges.
/// </summary>
public static class FoodPermissions
{
    /// <summary>Le code pointé du cahier des charges (§2), pour les journaux et les jetons.</summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUE CHAQUE RÔLE PEUT FAIRE, SANS DÉROGATION.
    ///
    /// LE CUISINIER NE VOIT NI L'ARGENT NI L'ADMINISTRATIF.
    ///
    /// C'est une exigence explicite du cahier (§2), et la seule qui y soit
    /// formulée comme une interdiction. Elle est tenue ici par construction :
    /// KitchenStaff n'a QUE <c>KitchenManage</c>. Ni le chiffre d'affaires, ni le
    /// personnel, ni les paramètres — et il ne peut pas non plus accepter une
    /// commande, geste commercial et non culinaire.
    ///
    /// UN ÉCART ASSUMÉ AVEC LE CAHIER, ET LE MOYEN DE LE COMBLER
    ///
    /// Le cahier attribue « horaires » au Manager. Les horaires vivent ici sous
    /// <c>SettingsManage</c>, aux côtés du minimum de commande et du mode
    /// d'acceptation — des paramètres COMMERCIAUX. Donner l'un donnerait les
    /// autres.
    ///
    /// Le Manager ne l'a donc pas par défaut. C'est précisément à cela que
    /// servent les dérogations : un propriétaire qui veut que son manager règle
    /// les horaires lui accorde <c>SettingsManage</c> nommément, et cette décision
    /// reste lisible dans les données au lieu d'être noyée dans un rôle.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
