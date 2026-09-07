namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>
/// Le sélecteur d'activité de HBA Partner (§11, §44).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE SEULE LISTE, DEUX UNIVERS — ET C'EST LA SEULE EXCEPTION À §45.
///
/// Partout ailleurs, HBAExpress et HBA Food ne se mélangent pas. Ici c'est
/// l'inverse qui serait faux : un partenaire qui gère une boutique ET un
/// restaurant doit voir les deux au même endroit, sinon il ne sait pas qu'il
/// peut basculer.
///
/// `Type` porte la distinction, et c'est lui qui décide du BFF à interroger
/// ensuite : STORE → Merchant BFF, RESTAURANT → Restaurant BFF.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record MerchantActivitiesDto(IReadOnlyList<MerchantActivityDto> Activities);

/// <param name="Type">« STORE » ou « RESTAURANT ».</param>
/// <param name="Role">
/// « OWNER » pour une boutique — un vendeur possède les siennes ; le rôle réel du
/// personnel pour un restaurant.
///
/// « OWNER » EST DÉDUIT, PAS LU.
///
/// merchant-service n'a AUCUN modèle de personnel : une boutique n'a qu'un
/// vendeur, et c'est le compte connecté. Le jour où une boutique aura une équipe,
/// ce champ devra venir du service — le déduire alors donnerait « OWNER » à un
/// caissier.
/// </param>
/// <param name="IsOpenNow">
/// <summary>Prend-elle des commandes en ce moment ? `null` si non calculable.</summary>
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
public sealed record MerchantDashboardDto(
    MerchantStoreDto Store,
    MerchantTodayDto Today,
    MerchantWalletDto? Wallet,
    IReadOnlyList<MerchantOrderDto> RecentOrders);

public sealed record MerchantStoreDto(
    Guid Id,
    string Name,
    string? LogoUrl,
    string Status,
    bool IsSelling,
    string ContactPhone);

/// <summary>
/// Les chiffres du jour.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CALCULÉS DANS LA PASSERELLE, FAUTE DE MIEUX — ET C'EST UN COMPROMIS.
///
/// order-service rend TOUTES les commandes du vendeur, sans période ni filtre.
/// Compter celles du jour suppose donc de recevoir l'historique complet à chaque
/// ouverture du tableau de bord. Cela fonctionne, et cela devient coûteux
/// exactement chez les vendeurs qui réussissent.
///
/// Ce n'est PAS une règle métier déplacée dans la passerelle : additionner des
/// montants déjà calculés par order-service n'invente aucun prix. Mais la lecture
/// appartient au service, et il faudra l'y rendre.
///
/// Manque à combler : <c>GET /api/sellers/{id}/orders/stats?from=&amp;to=</c>.
///
/// `LowStock` EST ABSENT, ET CE N'EST PAS UN OUBLI.
///
/// Le §12 le prévoit. <c>GET /api/inventory/low-stock</c> existe — mais SANS
/// filtre de propriétaire : il rend le stock faible de TOUTE la plateforme.
/// L'appeler depuis un BFF vendeur montrerait à un commerçant les ruptures de ses
/// concurrents. Manque à combler :
/// <c>GET /api/inventory/owners/{ownerId}/low-stock</c>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record MerchantTodayDto(
    int OrdersToday,
    decimal RevenueToday,
    decimal? AverageBasket,
    string? Currency,
    int OrdersToProcess);

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
