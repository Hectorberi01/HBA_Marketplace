namespace HBA.Food.Domain.Restaurants;

/// <summary>État d'un établissement HBA Food.</summary>
public enum RestaurantStatus
{
    /// <summary>Dossier créé, jamais validé.</summary>
    Draft = 0,

    /// <summary>En cours de vérification par HBA (documents, adresse, hygiène).</summary>
    PendingApproval = 1,

    /// <summary>En activité : peut recevoir des commandes pendant ses heures de service.</summary>
    Active = 2,

    /// <summary>Écarté par la plateforme.</summary>
    Suspended = 3,

    /// <summary>Le restaurateur a quitté la plateforme.</summary>
    Closed = 4
}

/// <summary>Pourquoi un restaurant ne prend pas de commande à cet instant.</summary>
public enum OrderingBlockedReason
{
    /// <summary>Le restaurant accepte les commandes.</summary>
    None = 0,

    /// <summary>
    /// Pas encore validé, suspendu ou fermé — voir <see cref="RestaurantStatus"/> .
    /// </summary>
    NotInService = 1,

    /// <summary>En dehors des heures de service.</summary>
    OutsideServiceHours = 2,

    /// <summary>
    /// Pause déclarée par le restaurateur, malgré des horaires ouverts : coup de
    /// feu, panne de gaz, rupture générale.
    /// </summary>
    TemporarilyPaused = 3,

    /// <summary>Aucun article disponible : le menu entier est épuisé.</summary>
    NothingAvailable = 4
}
