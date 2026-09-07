namespace HBA.Analytics.Application.RollUps;

// CHAQUE PARAMETRE EST DOCUMENTE, OU AUCUN — ET CE N'EST PAS DU ZELE.

/// <summary>Un jour de ventes d'un vendeur.</summary>
/// <param name="Day">
/// La journée UTC. Voir `JourneeAnalytique` pour ce que ça implique à Cotonou.
/// </param>
/// <param name="OrdersCount">Commandes confirmées où ce vendeur a une part.</param>
/// <param name="ItemsCount">Articles vendus, toutes lignes confondues.</param>
/// <param name="Revenue">
/// Somme des parts vendeur : le prix final des lignes de ce vendeur, REMISES
/// COMPRISES. CE N'EST PAS LE GAIN NET — la commission de la plateforme n'est pas
/// déduite, et les frais de livraison n'y sont pas.
/// </param>
/// <param name="GoodsOrdersCount">Dont marchandise.</param>
/// <param name="FoodOrdersCount">
/// Dont repas. Vaut zéro aujourd'hui : une commande de repas n'a pas de part
/// vendeur au sens de la place de marché — `BuildSellerShares` l'écarte
/// délibérément.
/// </param>
public sealed record SellerSalesPointDto(
    DateOnly Day,
    int OrdersCount,
    int ItemsCount,
    decimal Revenue,
    int GoodsOrdersCount,
    int FoodOrdersCount);

/// <summary>La série de ventes d'un vendeur sur une période, et ses totaux.</summary>
/// <param name="SellerId">Le vendeur, tel qu'il est désigné dans l'URL.</param>
/// <param name="From">Première journée rendue, incluse.</param>
/// <param name="To">Dernière journée rendue, incluse.</param>
/// <param name="Currency">La devise de TOUTE la série.</param>
/// <param name="Points">Un point par journée de la période, zéros compris.</param>
/// <param name="TotalOrders">Somme des commandes de la période.</param>
/// <param name="TotalItems">Somme des articles de la période.</param>
/// <param name="TotalRevenue">Somme des parts vendeur de la période.</param>
/// <param name="AverageOrderValue">
/// Panier moyen : <c> TotalRevenue / TotalOrders</c>, calculé ici plutôt que par le
/// client.
/// </param>
public sealed record SellerSalesSeriesDto(
    Guid SellerId,
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<SellerSalesPointDto> Points,
    int TotalOrders,
    int TotalItems,
    decimal TotalRevenue,
    decimal AverageOrderValue);

/// <summary>Un jour d'activité de la plateforme.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="OrdersCount">Commandes confirmées. Exact, quelle que soit la nature.</param>
/// <param name="ItemsCount">Articles vendus. Vaut zéro pour une commande de repas.</param>
/// <param name="Gmv">
/// VOLUME MARCHAND, et non chiffre d'affaires de la plateforme : c'est la somme des
/// parts vendeur.
/// </param>
/// <param name="GoodsOrdersCount">Dont marchandise.</param>
/// <param name="FoodOrdersCount">Dont repas.</param>
public sealed record PlatformActivityPointDto(
    DateOnly Day,
    int OrdersCount,
    int ItemsCount,
    decimal Gmv,
    int GoodsOrdersCount,
    int FoodOrdersCount);

/// <summary>La série d'activité de la plateforme sur une période, et ses totaux.</summary>
/// <param name="From">Première journée rendue, incluse.</param>
/// <param name="To">Dernière journée rendue, incluse.</param>
/// <param name="Currency">La devise de toute la série.</param>
/// <param name="Points">Un point par journée de la période, zéros compris.</param>
/// <param name="TotalOrders">Somme des commandes de la période.</param>
/// <param name="TotalItems">Somme des articles de la période.</param>
/// <param name="TotalGmv">Somme du volume marchand.</param>
public sealed record PlatformActivitySeriesDto(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<PlatformActivityPointDto> Points,
    int TotalOrders,
    int TotalItems,
    decimal TotalGmv);

/// <summary>Un jour d'inscriptions.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="Buyers">
/// Comptes créés. INCLUT les futurs vendeurs ET les futurs livreurs : les deux
/// s'inscrivent d'abord comme utilisateur. LES TROIS SÉRIES NE S'ADDITIONNENT PAS.
/// </param>
/// <param name="Sellers">Dossiers vendeur ouverts.</param>
/// <param name="Drivers">Comptes livreur ouverts, vérification non comprise.</param>
public sealed record SignupPointDto(DateOnly Day, int Buyers, int Sellers, int Drivers);

/// <summary>La série d'inscriptions sur une période.</summary>
/// <param name="From">Première journée rendue, incluse.</param>
/// <param name="To">Dernière journée rendue, incluse.</param>
/// <param name="Points">Un point par journée de la période, zéros compris.</param>
/// <param name="TotalBuyers">Somme des comptes créés.</param>
/// <param name="TotalSellers">Somme des dossiers vendeur ouverts.</param>
/// <param name="TotalDrivers">Somme des comptes livreur ouverts.</param>
public sealed record SignupSeriesDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SignupPointDto> Points,
    int TotalBuyers,
    int TotalSellers,
    int TotalDrivers);
