namespace HBA.Gateway.Application.Bff.Restaurant;

/// <summary>Tableau de bord d'un restaurant (§13).</summary>
public sealed record RestaurantDashboardDto(
    RestaurantHeaderDto Restaurant,
    RestaurantServiceDto Service,
    RestaurantWalletDto? Wallet,
    RestaurantKitchenSummaryDto Kitchen);

public sealed record RestaurantHeaderDto(
    Guid Id,
    string Name,
    string Status,
    string Role,
    IReadOnlyList<string> Permissions);

/// <summary>L'état du service, tel qu'un restaurateur le lit d'un coup d'œil.</summary>
public sealed record RestaurantServiceDto(bool AcceptsOrdersNow, string BlockedReason);

public sealed record RestaurantWalletDto(
    decimal PendingBalance,
    decimal AvailableBalance,
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Ce qui attend en cuisine, en trois nombres.</summary>
/// <remarks>
/// DES COMPTEURS, PAS LES TICKETS.
///
/// Le tableau de bord dit COMBIEN ; l'écran cuisine dit QUOI. Y transporter les
/// tickets complets — plats, options, notes client — alourdirait l'accueil d'un
/// contenu que personne n'y lit, et le dupliquerait à deux endroits.
/// </remarks>
public sealed record RestaurantKitchenSummaryDto(int Pending, int Preparing, int Ready);

/// <summary>
/// Écran de cuisine — KDS (§14).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// NI PORTEFEUILLE, NI COMMISSION, NI REVENU, NI DOCUMENT COMMERCIAL.
///
/// Le §14 l'écrit, et la raison est concrète : cet écran tourne sur une tablette
/// posée en cuisine, allumée toute la journée, que voient les cuisiniers, les
/// extras et parfois les livreurs. Le chiffre d'affaires du restaurant n'a rien à
/// y faire.
///
/// Ce type ne porte donc AUCUN montant — pas même le total de la commande. Un
/// cuisinier prépare des plats, il n'encaisse pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record RestaurantKitchenDto(
    Guid RestaurantId,
    Guid? StationId,
    IReadOnlyList<KitchenStationDto> Stations,
    IReadOnlyList<KitchenTicketDto> Pending,
    IReadOnlyList<KitchenTicketDto> Preparing,
    IReadOnlyList<KitchenTicketDto> Ready);

public sealed record KitchenStationDto(Guid Id, string Name, bool IsActive);

/// <param name="ElapsedSeconds">
/// Temps écoulé depuis la réception.
///
/// CALCULÉ ICI, ET C'EST UN CHOIX DISCUTABLE.
///
/// Le service rend <c>ReceivedAtUtc</c>. Le client pourrait faire la
/// soustraction, mais dépendrait alors de l'horloge de la tablette — souvent
/// fausse sur du matériel bon marché laissé branché des mois. Le compteur du
/// serveur est la seule mesure commune à toute la cuisine.
///
/// Il vieillit entre l'envoi et l'affichage : c'est SignalR (§40) qui le
/// rafraîchit, pas un rechargement.
/// </param>
public sealed record KitchenTicketDto(
    Guid FoodOrderId,
    Guid OrderId,
    string Status,
    int Priority,
    int? EstimatedPreparationMinutes,
    DateTime ReceivedAtUtc,
    int ElapsedSeconds,
    string? CustomerNote,
    int OtherStationsPending,
    IReadOnlyList<KitchenTicketItemDto> Items);

public sealed record KitchenTicketItemDto(
    Guid Id,
    string Name,
    int Quantity,
    string? Notes,
    string Status,
    Guid? PreparationStationId,
    int PreparationMinutes,
    IReadOnlyList<string> Options);
