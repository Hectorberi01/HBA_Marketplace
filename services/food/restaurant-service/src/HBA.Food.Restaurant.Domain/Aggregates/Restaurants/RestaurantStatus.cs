namespace HBA.Food.Domain.Restaurants;

/// <summary>
/// État d'un établissement HBA Food.
///
/// NE PAS CONFONDRE AVEC « OUVERT MAINTENANT ».
///
/// Ce statut dit si l'établissement est EN ACTIVITÉ sur la plateforme. Savoir
/// s'il prend une commande à cet instant dépend en plus de ses horaires et de sa
/// pause éventuelle — voir <c>Restaurant.CanAcceptOrders</c>.
///
/// Confondre les deux mènerait à basculer le statut à chaque fermeture du soir,
/// et l'historique ne dirait plus jamais si un restaurant a été suspendu ou s'il
/// dormait simplement.
/// </summary>
public enum RestaurantStatus
{
    /// <summary>Dossier créé, jamais validé. N'apparaît pas dans la vitrine.</summary>
    Draft = 0,

    /// <summary>En cours de vérification par HBA (documents, adresse, hygiène).</summary>
    PendingApproval = 1,

    /// <summary>En activité : peut recevoir des commandes pendant ses heures de service.</summary>
    Active = 2,

    /// <summary>
    /// Écarté par la plateforme. Ne peut pas se rétablir lui-même — sinon la
    /// sanction ne durerait que le temps d'un clic.
    /// </summary>
    Suspended = 3,

    /// <summary>Le restaurateur a quitté la plateforme. Réversible par l'exploitation.</summary>
    Closed = 4
}

/// <summary>
/// Pourquoi un restaurant ne prend pas de commande à cet instant.
///
/// UN BOOLÉEN AURAIT SUFFI AU CODE, PAS À L'ACHETEUR.
///
/// « Indisponible » sur un écran de commande est la réponse la plus frustrante
/// qui soit : le client ne sait pas s'il doit revenir dans dix minutes, demain,
/// ou jamais. Chaque valeur d'ici devient une phrase différente à l'écran.
/// </summary>
public enum OrderingBlockedReason
{
    /// <summary>Le restaurant accepte les commandes.</summary>
    None = 0,

    /// <summary>Pas encore validé, suspendu ou fermé — voir <see cref="RestaurantStatus"/>.</summary>
    NotInService = 1,

    /// <summary>En dehors des heures de service. « Rouvre demain à 11 h. »</summary>
    OutsideServiceHours = 2,

    /// <summary>
    /// Pause déclarée par le restaurateur, malgré des horaires ouverts : coup de
    /// feu, panne de gaz, rupture générale. « De retour dans 30 minutes. »
    /// </summary>
    TemporarilyPaused = 3,

    /// <summary>Aucun article disponible : le menu entier est épuisé.</summary>
    NothingAvailable = 4
}
