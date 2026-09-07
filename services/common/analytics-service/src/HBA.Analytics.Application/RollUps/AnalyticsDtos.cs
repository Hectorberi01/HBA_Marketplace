namespace HBA.Analytics.Application.RollUps;

// ═════════════════════════════════════════════════════════════════════════════
// CHAQUE PARAMETRE EST DOCUMENTE, OU AUCUN — ET CE N'EST PAS DU ZELE.
//
// `Directory.Build.props` allume `GenerateDocumentationFile` pour tout le depot
// et NE TAIT PAS CS1573 : documenter une partie des parametres d'un
// enregistrement positionnel produit un avertissement par parametre oublie. Le
// choix est ecrit la-bas et il est juste — CS1573 signale precisement une
// documentation qui NE SERA PAS RENDUE.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un jour de ventes d'un vendeur.</summary>
/// <param name="Day">La journée UTC. Voir `JourneeAnalytique` pour ce que ça implique à Cotonou.</param>
/// <param name="OrdersCount">Commandes confirmées où ce vendeur a une part.</param>
/// <param name="ItemsCount">Articles vendus, toutes lignes confondues.</param>
/// <param name="Revenue">
/// Somme des parts vendeur : le prix final des lignes de ce vendeur, REMISES
/// COMPRISES. CE N'EST PAS LE GAIN NET — la commission de la plateforme n'est pas
/// déduite, et les frais de livraison n'y sont pas. Un vendeur qui compare ce
/// chiffre à son relevé de versement trouvera un écart, et l'écart est
/// exactement la commission.
/// </param>
/// <param name="GoodsOrdersCount">Dont marchandise.</param>
/// <param name="FoodOrdersCount">
/// Dont repas. Vaut zéro aujourd'hui : une commande de repas n'a pas de part
/// vendeur au sens de la place de marché — `BuildSellerShares` l'écarte
/// délibérément. Le champ existe pour que le graphe n'ait pas à changer de forme
/// le jour où les restaurants deviendront des vendeurs comme les autres.
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
/// <param name="Currency">
/// La devise de TOUTE la série. Elle est rendue parce qu'elle est choisie par
/// défaut quand l'appelant ne la donne pas — sans elle, une série ne montrant
/// qu'une devise sur deux aurait l'air complète.
/// </param>
/// <param name="Points">Un point par journée de la période, zéros compris.</param>
/// <param name="TotalOrders">Somme des commandes de la période.</param>
/// <param name="TotalItems">Somme des articles de la période.</param>
/// <param name="TotalRevenue">Somme des parts vendeur de la période.</param>
/// <param name="AverageOrderValue">
/// Panier moyen : <c>TotalRevenue / TotalOrders</c>, calculé ici plutôt que par
/// le client. Deux clients — web et mobile — le calculeraient deux fois, et la
/// division par zéro d'une période sans vente serait à écrire deux fois aussi.
/// Vaut zéro quand il n'y a aucune commande.
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
/// VOLUME MARCHAND, et non chiffre d'affaires de la plateforme : c'est la somme
/// des parts vendeur. Frais de livraison et commission n'y sont pas, et une
/// commande de repas y compte pour zéro faute de parts. C'est une limite du
/// CONTRAT — `OrderConfirmed` ne porte pas le total payé par l'acheteur — et
/// elle se lève d'un champ optionnel `GrandTotal`, comme le lot 2 en ajoute
/// trois autres.
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
/// <param name="TotalGmv">Somme du volume marchand. Voir <see cref="PlatformActivityPointDto"/>.</param>
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
/// Comptes créés. INCLUT les futurs vendeurs : un vendeur s'inscrit d'abord comme
/// utilisateur. Voir `NatureDInscription`.
/// </param>
/// <param name="Sellers">Dossiers vendeur ouverts.</param>
public sealed record SignupPointDto(DateOnly Day, int Buyers, int Sellers);

/// <summary>La série d'inscriptions sur une période.</summary>
/// <param name="From">Première journée rendue, incluse.</param>
/// <param name="To">Dernière journée rendue, incluse.</param>
/// <param name="Points">Un point par journée de la période, zéros compris.</param>
/// <param name="TotalBuyers">Somme des comptes créés.</param>
/// <param name="TotalSellers">Somme des dossiers vendeur ouverts.</param>
/// <remarks>
/// PAS DE TOTAL « COMPTES CRÉÉS » : <c>TotalBuyers + TotalSellers</c> surcompte
/// les vendeurs, qui apparaissent dans les deux. Rendre un total qui n'a pas de
/// sens ferait tracer une troisième courbe à qui la lit.
/// </remarks>
public sealed record SignupSeriesDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SignupPointDto> Points,
    int TotalBuyers,
    int TotalSellers);
