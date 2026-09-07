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
/// <param name="Store">La boutique — seule dépendance critique de l'écran.</param>
/// <param name="Today">
/// Les chiffres du jour. TOUJOURS PRÉSENT — ses champs analytiques valent `null`
/// quand analytics est muet, mais `OrdersToProcess` vient d'order-service et
/// survit. Rendre l'objet entier `null` ferait disparaître de l'écran un compte
/// que la passerelle avait pourtant obtenu.
/// </param>
/// <param name="Sales">
/// La courbe des trente derniers jours. `null` si analytics est muet.
///
/// C'EST LE GRAPHE, ET IL EST DANS LE TABLEAU DE BORD PLUTÔT QU'À CÔTÉ.
///
/// Une courbe demandée par un second appel ferait deux allers-retours pour un
/// écran qui s'ouvre d'un coup, et laisserait le graphe arriver après les
/// tuiles. `GET /api/v1/bff/merchant/analytics` existe pour l'écran DÉDIÉ, où
/// la période se change — pas pour celui-ci.
/// </param>
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
/// <param name="OrdersToday">
/// Commandes CONFIRMÉES du jour — donc payées. `null` si analytics est muet.
/// </param>
/// <param name="RevenueToday">
/// Part du vendeur sur ces commandes, remises comprises, commission non déduite.
/// </param>
/// <param name="AverageBasket">
/// Panier moyen. `0` un jour sans vente : c'est analytics qui le calcule et qui
/// garde la division, pas la passerelle.
/// </param>
/// <param name="Currency">La devise de la série.</param>
/// <param name="OrdersToProcess">
/// Commandes en attente d'un geste du vendeur. Vient d'order-service, PAS
/// d'analytics — voir l'encadré.
/// </param>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CES CHIFFRES VIENNENT MAINTENANT D'analytics-service, ET ILS ONT CHANGÉ DE
///    SENS. IL FAUT LE SAVOIR AVANT DE COMPARER À UNE CAPTURE D'ÉCRAN D'HIER.
///
/// AVANT : la passerelle demandait la liste des commandes du vendeur à
/// order-service et filtrait sur `CreatedAtUtc.Date == aujourd'hui`. Elle comptait
/// donc les commandes PLACÉES, quel que soit leur statut — y compris celles que
/// personne n'a jamais payées.
///
/// MAINTENANT : le roll-up compte les commandes CONFIRMÉES, à l'instant de leur
/// confirmation. Une commande placée à 23 h 50 et payée à 0 h 10 change de
/// journée ; une commande abandonnée à l'écran de paiement disparaît du chiffre.
/// C'est le bon sens pour un « chiffre d'affaires du jour » — mais le nombre
/// affiché BAISSE, et sans cette note personne ne saurait pourquoi.
///
/// LE MONTANT, LUI, N'A PAS BOUGÉ D'UN FRANC.
///
/// `OrderSellerShare.Amount` et `OrderMapper.ToSellerSummary` somment tous deux
/// les `LineTotal` des lignes du vendeur. Vérifié dans le domaine d'order-service
/// plutôt que supposé : c'est ce qui rend le remplacement sûr.
///
/// L'ANCIEN CALCUL AVAIT UN DÉFAUT QUE SON ENCADRÉ NE DISAIT PAS.
///
/// Cet encadré affirmait que le calcul « devient coûteux exactement chez les
/// vendeurs qui réussissent ». Ce n'était plus vrai : `ListOrdersBySellerQuery`
/// borne sa lecture à 50 commandes par défaut, 200 au maximum. Le vrai défaut
/// était l'inverse — la TRONCATURE. Au-delà de cinquante commandes récentes, les
/// chiffres du jour et le compte « à traiter » étaient calculés sur un
/// échantillon, et rien ne le signalait. Un roll-up est exact quel que soit le
/// volume.
///
/// `OrdersToProcess` RESTE CALCULÉ SUR CETTE LISTE TRONQUÉE.
///
/// C'est un compte de STATUTS à l'instant présent, pas un agrégat journalier :
/// aucun roll-up ne peut le rendre. Il garde donc la limite de cinquante, et
/// c'est la raison pour laquelle l'appel à order-service n'a pas disparu de cet
/// écran. Manque à combler, et il n'a pas changé :
/// <c>GET /api/sellers/{id}/orders?status=&amp;page=</c>.
///
/// `LowStock` EST TOUJOURS ABSENT, ET CE N'EST TOUJOURS PAS UN OUBLI.
///
/// `GET /api/inventory/low-stock` n'a aucun filtre de propriétaire : il rend le
/// stock faible de TOUTE la plateforme. L'appeler depuis un BFF vendeur
/// montrerait à un commerçant les ruptures de ses concurrents. Manque à combler :
/// <c>GET /api/inventory/owners/{ownerId}/low-stock</c>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
/// absent. Une courbe à laquelle il manque des points relie le 3 au 7 par une
/// droite et donne à lire une activité continue là où il n'y en a eu aucune.
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
