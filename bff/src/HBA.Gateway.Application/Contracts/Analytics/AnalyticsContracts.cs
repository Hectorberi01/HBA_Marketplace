namespace HBA.Gateway.Application.Contracts.Analytics;

// ═════════════════════════════════════════════════════════════════════════════
// MIROIRS DES RÉPONSES D'analytics-service, ET NON UNE RÉFÉRENCE À SON PROJET.
//
// C'est la règle de tous les `Contracts/` de cette passerelle : elle ne référence
// AUCUN assemblage de service. Un `ProjectReference` vers
// `HBA.Analytics.Application` ferait entrer dans la passerelle le domaine du
// service, ses entités EF et ses dépendances — et le jour où l'on voudrait
// déployer les deux séparément, la frontière n'existerait plus.
//
// Ce que ça coûte : deux déclarations à tenir d'accord. Ce qui les tient, c'est
// le désérialiseur — un champ renommé en face rend `default` ici, sans
// exception. `ServiceHttpClient.GetAsync` journalise « contrat rompu » quand le
// TYPE ne colle plus ; il ne peut rien dire d'un champ devenu nul en silence.
//
// LES CHAMPS OMIS LE SONT DÉLIBÉRÉMENT. Le service rend aussi `ItemsCount` par
// jour ; aucun écran ne le trace aujourd'hui, et un miroir n'a pas à recopier ce
// que personne ne lit. Ils restent disponibles par la route directe, relayée par
// `ReverseProxy`.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un jour de ventes d'un vendeur.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="OrdersCount">Commandes CONFIRMÉES — donc payées.</param>
/// <param name="Revenue">Part du vendeur, remises comprises, commission non déduite.</param>
public sealed record SellerSalesPoint(DateOnly Day, int OrdersCount, decimal Revenue);

/// <summary>La série de ventes d'un vendeur, telle qu'analytics la rend.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Currency">Devise de toute la série.</param>
/// <param name="Points">Un point par jour, zéros compris.</param>
/// <param name="TotalOrders">Somme des commandes de la période.</param>
/// <param name="TotalRevenue">Somme des parts vendeur de la période.</param>
/// <param name="AverageOrderValue">Panier moyen, déjà calculé et gardé contre la division par zéro.</param>
public sealed record SellerSalesSeries(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<SellerSalesPoint> Points,
    int TotalOrders,
    decimal TotalRevenue,
    decimal AverageOrderValue);

/// <summary>Un jour d'activité de la plateforme.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="OrdersCount">Commandes confirmées, toutes natures.</param>
/// <param name="Gmv">
/// VOLUME MARCHAND, pas chiffre d'affaires : somme des parts vendeur, sans les
/// frais de livraison ni la commission, et zéro pour une commande de repas.
/// </param>
/// <param name="GoodsOrdersCount">Dont marchandise.</param>
/// <param name="FoodOrdersCount">Dont repas.</param>
public sealed record PlatformActivityPoint(
    DateOnly Day, int OrdersCount, decimal Gmv, int GoodsOrdersCount, int FoodOrdersCount);

/// <summary>La série d'activité de la plateforme.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Currency">Devise de toute la série.</param>
/// <param name="Points">Un point par jour, zéros compris.</param>
/// <param name="TotalOrders">Somme des commandes de la période.</param>
/// <param name="TotalGmv">Somme du volume marchand de la période.</param>
public sealed record PlatformActivitySeries(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<PlatformActivityPoint> Points,
    int TotalOrders,
    decimal TotalGmv);

/// <summary>Un jour d'inscriptions.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="Buyers">Comptes créés. INCLUT les futurs vendeurs.</param>
/// <param name="Sellers">Dossiers vendeur ouverts.</param>
public sealed record SignupPoint(DateOnly Day, int Buyers, int Sellers);

/// <summary>La série d'inscriptions.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Points">Un point par jour, zéros compris.</param>
/// <param name="TotalBuyers">Somme des comptes créés.</param>
/// <param name="TotalSellers">Somme des dossiers vendeur ouverts.</param>
/// <remarks>
/// PAS DE TOTAL « COMPTES CRÉÉS » : les deux séries ne s'additionnent pas — un
/// vendeur est dans les deux. Le service ne le rend pas, et le miroir ne
/// l'invente pas.
/// </remarks>
public sealed record SignupSeries(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SignupPoint> Points,
    int TotalBuyers,
    int TotalSellers);
