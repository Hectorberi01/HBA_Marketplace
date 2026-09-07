namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>Le sélecteur d'activité de HBA Partner (§11, §44).</summary>
public sealed record MerchantActivitiesDto(IReadOnlyList<MerchantActivityDto> Activities);

/// <param name="Type">« STORE » ou « RESTAURANT ».</param>
/// <param name="Role">
/// « OWNER » pour une boutique — un vendeur possède les siennes ; le rôle réel du
/// personnel pour un restaurant.
/// </param>
/// <param name="IsOpenNow">
/// <summary> Prend-elle des commandes en ce moment ? `null` si non
/// calculable.</summary>
/// </param>
public sealed record MerchantActivityDto(
    string Type,
    Guid Id,
    string Name,
    string? LogoUrl,
    string Role,
    string Status,
    bool? IsOpenNow);

/// <summary>Tableau de bord d'une boutique (§12).</summary>
/// <param name="Store">La boutique — seule dépendance critique de l'écran.</param>
/// <param name="Today">
/// Les chiffres du jour. TOUJOURS PRÉSENT — ses champs analytiques valent `null`
/// quand analytics est muet, mais `OrdersToProcess` vient d'order-service et
/// survit.
/// </param>
/// <param name="Sales">La courbe des trente derniers jours.</param>
/// <param name="Wallet">Le portefeuille. `null` si financial est muet.</param>
/// <param name="RecentOrders">Les dernières commandes. Vide si order est muet.</param>
public sealed record MerchantDashboardDto(
    MerchantStoreDto Store,
    MerchantTodayDto Today,
    MerchantSalesSeriesDto? Sales,
    MerchantWalletDto? Wallet,
    IReadOnlyList<MerchantOrderDto> RecentOrders);

public sealed record MerchantStoreDto(
    Guid Id,
    string Name,
    string? LogoUrl,
    string Status,
    bool IsSelling,
    string ContactPhone);

/// <summary>Les chiffres du jour.</summary>
/// <param name="OrdersToday">Commandes CONFIRMÉES du jour — donc payées.</param>
/// <param name="RevenueToday">
/// Part du vendeur sur ces commandes, remises comprises, commission non déduite.
/// </param>
/// <param name="AverageBasket">
/// Panier moyen. `0` un jour sans vente : c'est analytics qui le calcule et qui
/// garde la division, pas la passerelle.
/// </param>
/// <param name="Currency">La devise de la série.</param>
/// <param name="OrdersToProcess">Commandes en attente d'un geste du vendeur.</param>
public sealed record MerchantTodayDto(
    int? OrdersToday,
    decimal? RevenueToday,
    decimal? AverageBasket,
    string? Currency,
    int OrdersToProcess);

/// <summary>Un jour de la courbe de ventes.</summary>
/// <param name="Day">La journée, en UTC.</param>
/// <param name="OrdersCount">Commandes confirmées ce jour-là.</param>
/// <param name="Revenue">Part du vendeur ce jour-là.</param>
public sealed record MerchantSalesPointDto(DateOnly Day, int OrdersCount, decimal Revenue);

/// <summary>La courbe de ventes du vendeur sur une période.</summary>
/// <param name="From">Première journée, incluse.</param>
/// <param name="To">Dernière journée, incluse.</param>
/// <param name="Currency">Devise de toute la série.</param>
/// <param name="Points">
/// Un point par jour, SANS TROU : un jour sans vente vaut zéro, il n'est pas
/// absent.
/// </param>
/// <param name="TotalOrders">Somme des commandes de la période.</param>
/// <param name="TotalRevenue">Somme des parts vendeur de la période.</param>
/// <param name="AverageOrderValue">Panier moyen de la période.</param>
public sealed record MerchantSalesSeriesDto(
    DateOnly From,
    DateOnly To,
    string Currency,
    IReadOnlyList<MerchantSalesPointDto> Points,
    int TotalOrders,
    decimal TotalRevenue,
    decimal AverageOrderValue);

public sealed record MerchantWalletDto(
    decimal PendingBalance,
    decimal AvailableBalance,
    decimal PendingWithdrawal,
    string Currency);

public sealed record MerchantOrderDto(
    Guid Id,
    string Status,
    decimal GrandTotal,
    string Currency,
    DateTime CreatedAtUtc);
