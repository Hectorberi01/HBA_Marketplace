namespace HBA.Gateway.Application.Contracts.Analytics;

// MIROIRS DES RÉPONSES D'analytics-service, ET NON UNE RÉFÉRENCE À SON PROJET.

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
/// <param name="AverageOrderValue">
/// Panier moyen, déjà calculé et gardé contre la division par zéro.
/// </param>
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
/// <param name="Buyers">Comptes créés. INCLUT les futurs vendeurs ET livreurs.</param>
/// <param name="Sellers">Dossiers vendeur ouverts.</param>
/// <param name="Drivers">Comptes livreur ouverts.</param>
public sealed record SignupPoint(DateOnly Day, int Buyers, int Sellers, int Drivers);

/// <summary>La série d'inscriptions.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Points">Un point par jour, zéros compris.</param>
/// <param name="TotalBuyers">Somme des comptes créés.</param>
/// <param name="TotalSellers">Somme des dossiers vendeur ouverts.</param>
/// <param name="TotalDrivers">Somme des comptes livreur ouverts.</param>
public sealed record SignupSeries(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SignupPoint> Points,
    int TotalBuyers,
    int TotalSellers,
    int TotalDrivers);

/// <summary>Un jour de tentatives de paiement, toutes issues confondues.</summary>
/// <param name="Day">La journée UTC.</param>
/// <param name="Captured">Tentatives encaissées ce jour-là.</param>
/// <param name="Failed">Tentatives échouées ce jour-là.</param>
/// <param name="CapturedAmount">Volume encaissé ce jour-là.</param>
public sealed record PaymentPoint(DateOnly Day, int Captured, int Failed, decimal CapturedAmount);

/// <summary>Ce qu'un prestataire a traité sur la période.</summary>
/// <param name="Provider">Le prestataire, en minuscules. « inconnu » avant le lot 2.</param>
/// <param name="Captured">Tentatives encaissées.</param>
/// <param name="Failed">Tentatives échouées.</param>
/// <param name="CapturedAmount">Volume encaissé.</param>
/// <param name="FailureRate">Failed / (Failed + Captured). `null` sans tentative.</param>
public sealed record PaymentProvider(
    string Provider, int Captured, int Failed, decimal CapturedAmount, decimal? FailureRate);

/// <summary>Les paiements de la plateforme sur une période.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Currency">La devise retenue.</param>
/// <param name="Points">La courbe jour par jour, tous prestataires confondus.</param>
/// <param name="Providers">Le classement par prestataire, du plus gros volume au plus petit.</param>
/// <param name="TotalCaptured">Tentatives encaissées sur la période.</param>
/// <param name="TotalFailed">Tentatives échouées sur la période.</param>
/// <param name="FailureRate">Taux d'échec global. `null` sans aucune tentative.</param>
public sealed record PaymentSeries(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<PaymentPoint> Points,
    IReadOnlyList<PaymentProvider> Providers,
    int TotalCaptured,
    int TotalFailed,
    decimal? FailureRate);
