namespace HBA.Food.Domain.Orders;

/// <summary>LE CYCLE DE VIE OPÉRATIONNEL D'UNE COMMANDE FOOD (cahier des charges §10).</summary>
public enum FoodOrderStatus
{
    /// <summary>Reçue, en attente de la décision du restaurant.</summary>
    PendingRestaurantAcceptance = 0,

    /// <summary>Le restaurant a dit oui. Le ticket de cuisine existe.</summary>
    Accepted = 1,

    /// <summary>Le restaurant a dit non, avec un motif.</summary>
    Rejected = 2,

    /// <summary>La cuisine a commencé. Il n'est plus temps d'annuler sans frais.</summary>
    Preparing = 3,

    /// <summary>Tout est prêt, sur le passe.</summary>
    ReadyForPickup = 4,

    /// <summary>Le livreur a le sac. La responsabilité passe à HBA Delivery.</summary>
    PickedUp = 5,

    /// <summary>Remis au client. État TERMINAL.</summary>
    Delivered = 6,

    /// <summary>Annulée — par le client, par l'exploitation, par un incident.</summary>
    Cancelled = 7
}

/// <summary>Pourquoi un restaurant refuse une commande (cahier §11).</summary>
public enum FoodRejectionReason
{
    /// <summary>Rupture générale. <c>OUT_OF_STOCK</c></summary>
    OutOfStock = 0,

    /// <summary>Cuisine saturée : le délai ne serait pas tenable.</summary>
    KitchenOverloaded = 1,

    /// <summary>Fermeture imminente. <c>CLOSING</c></summary>
    Closing = 2,

    /// <summary>Un article précis manque.</summary>
    ItemUnavailable = 3,

    /// <summary>Panne, coupure, incident.</summary>
    TechnicalProblem = 4,

    /// <summary>Autre chose — c'est le commentaire qui porte alors l'information.</summary>
    Other = 5
}

/// <summary>L'état d'un article sur le ticket de cuisine (cahier §12).</summary>
public enum KitchenItemStatus
{
    /// <summary>À préparer.</summary>
    Pending = 0,

    /// <summary>En cours.</summary>
    Preparing = 1,

    /// <summary>Prêt, sur le passe.</summary>
    Ready = 2
}

/// <summary>L'état du ticket de cuisine (cahier §12).</summary>
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
