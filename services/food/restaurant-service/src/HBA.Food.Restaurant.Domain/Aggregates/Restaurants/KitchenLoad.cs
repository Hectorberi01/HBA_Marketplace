namespace HBA.Food.Domain.Restaurants;

/// <summary>Comment le restaurant traite une commande qui arrive (cahier des charges §3).</summary>
public enum OrderAcceptanceMode
{
    /// <summary>Le restaurant décide, commande par commande.</summary>
    Manual = 0,

    // `Automatic` CI-DESSOUS EST INATTEIGNABLE (lot 9.2) : aucune route ne permet
    // au restaurateur de basculer son établissement en acceptation automatique.

    /// <summary>
    /// Acceptation immédiate : la commande part en cuisine sans attendre personne.
    /// </summary>
    Automatic = 1
}

/// <summary>La charge de la cuisine (cahier §14).</summary>
public enum KitchenLoadLevel
{
    /// <summary>La cuisine suit.</summary>
    Normal = 0,

    /// <summary>« Forte demande » — le cahier nomme cet affichage (§14).</summary>
    High = 1,

    /// <summary>Au plafond. L'auto-acceptation se coupe.</summary>
    Saturated = 2
}

/// <summary>L'ÉTAT DE CHARGE, ET SES QUATRE CONSÉQUENCES (cahier §14).</summary>
/// <param name="Level">Ce que le client voit.</param>
/// <param name="ActiveOrders">Commandes en cours au moment du calcul.</param>
/// <param name="ExtraWaitMinutes">
/// À AJOUTER à l'ETA. Dérivé du rythme du restaurant lui-même — voir
/// <see cref="Restaurant.AssessLoad"/> — et non d'une constante globale.
/// </param>
/// <param name="AutoAcceptSuspended">L'auto-acceptation est coupée.</param>
/// <param name="BlocksNewOrders">Le restaurant refuse les nouvelles commandes.</param>
public sealed record KitchenLoad(
    KitchenLoadLevel Level,
    int ActiveOrders,
    int ExtraWaitMinutes,
    bool AutoAcceptSuspended,
    bool BlocksNewOrders)
{
    /// <summary>« Forte demande » : l'affichage nommé par le §14.</summary>
    public bool ShowsHighDemand => Level != KitchenLoadLevel.Normal;
}
