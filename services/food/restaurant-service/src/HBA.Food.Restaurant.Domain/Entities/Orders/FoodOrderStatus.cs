namespace HBA.Food.Domain.Orders;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CYCLE DE VIE OPÉRATIONNEL D'UNE COMMANDE FOOD (cahier des charges §10).
///
/// CE N'EST PAS LE STATUT COMMERCIAL DE LA COMMANDE.
///
/// Le cahier le dit en une phrase : « Order Service reste propriétaire de la
/// commande commerciale globale. Food Service gère sa partie opérationnelle
/// restaurant et cuisine. »
///
/// Le module Ordering sait si c'est payé, remboursé, facturé. Celui-ci sait si
/// c'est accepté, en cuisson, prêt à emporter. Les fondre aurait obligé le
/// restaurateur à comprendre « AwaitingPayment » et le comptable à comprendre
/// « Preparing » — et chaque changement de l'un aurait risqué de casser l'autre.
///
/// Les deux se rejoignent par événements, jamais par une colonne partagée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum FoodOrderStatus
{
    /// <summary>
    /// Reçue, en attente de la décision du restaurant.
    ///
    /// C'est l'état où le temps compte le plus : le cahier suit
    /// <c>restaurant_acceptance_time_seconds</c> comme métrique (§21), et un
    /// client qui attend dix minutes une acceptation annule.
    /// </summary>
    PendingRestaurantAcceptance = 0,

    /// <summary>Le restaurant a dit oui. Le ticket de cuisine existe.</summary>
    Accepted = 1,

    /// <summary>Le restaurant a dit non, avec un motif. État TERMINAL.</summary>
    Rejected = 2,

    /// <summary>La cuisine a commencé. Il n'est plus temps d'annuler sans frais.</summary>
    Preparing = 3,

    /// <summary>Tout est prêt, sur le passe. C'est CET état qui appelle un livreur.</summary>
    ReadyForPickup = 4,

    /// <summary>Le livreur a le sac. La responsabilité passe à HBA Delivery.</summary>
    PickedUp = 5,

    /// <summary>Remis au client. État TERMINAL.</summary>
    Delivered = 6,

    /// <summary>Annulée — par le client, par l'exploitation, par un incident. État TERMINAL.</summary>
    Cancelled = 7
}

/// <summary>
/// Pourquoi un restaurant refuse une commande (cahier §11).
///
/// UNE ÉNUMÉRATION ET NON UN TEXTE LIBRE, pour une raison qui n'apparaît qu'au
/// bout de quelques mois : le cahier veut suivre « les restaurants à fort taux de
/// refus » (§22). Un champ libre rend cette question incalculable — « plus de
/// poulet », « rupture », « pas de poulet ce soir » seraient trois motifs
/// distincts.
///
/// Le commentaire libre existe à côté, pour ce que l'énumération ne dit pas.
/// </summary>
public enum FoodRejectionReason
{
    /// <summary>Rupture générale. <c>OUT_OF_STOCK</c></summary>
    OutOfStock = 0,

    /// <summary>Cuisine saturée : le délai ne serait pas tenable. <c>KITCHEN_OVERLOADED</c></summary>
    KitchenOverloaded = 1,

    /// <summary>Fermeture imminente. <c>CLOSING</c></summary>
    Closing = 2,

    /// <summary>Un article précis manque. <c>ITEM_UNAVAILABLE</c></summary>
    ItemUnavailable = 3,

    /// <summary>Panne, coupure, incident. <c>TECHNICAL_PROBLEM</c></summary>
    TechnicalProblem = 4,

    /// <summary>Autre chose — c'est le commentaire qui porte alors l'information. <c>OTHER</c></summary>
    Other = 5
}

/// <summary>
/// L'état d'un article sur le ticket de cuisine (cahier §12).
///
/// PAR ARTICLE, ET NON SEULEMENT PAR TICKET. C'est ce que le §13 exige : une
/// commande partie sur deux postes — deux burgers au grill, deux cocas au bar —
/// n'est prête que quand LES DEUX postes ont fini. Sans état par article, le
/// grillardin qui termine marquerait toute la commande prête, et le livreur
/// repartirait sans les boissons.
/// </summary>
public enum KitchenItemStatus
{
    /// <summary>À préparer.</summary>
    Pending = 0,

    /// <summary>En cours.</summary>
    Preparing = 1,

    /// <summary>Prêt, sur le passe.</summary>
    Ready = 2
}

/// <summary>
/// L'état du ticket de cuisine (cahier §12).
///
/// DÉRIVÉ DES ARTICLES, JAMAIS SAISI DIRECTEMENT — voir
/// <c>FoodOrder.KitchenStatus</c>. Un statut de ticket qu'on pourrait poser à la
/// main pourrait contredire ses propres lignes, et le §20 interdit précisément
/// « Ready sans passage par le workflow de préparation ».
/// </summary>
public enum KitchenTicketStatus
{
    /// <summary>Aucun article commencé.</summary>
    Pending = 0,

    /// <summary>Au moins un article commencé, tous ne sont pas prêts.</summary>
    Preparing = 1,

    /// <summary>Tous les articles sont prêts — toutes stations confondues.</summary>
    Ready = 2,

    /// <summary>La commande a été annulée : la cuisine doit s'arrêter.</summary>
    Cancelled = 3
}
