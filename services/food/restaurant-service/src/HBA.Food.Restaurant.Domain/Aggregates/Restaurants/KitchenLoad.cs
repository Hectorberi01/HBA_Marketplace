namespace HBA.Food.Domain.Restaurants;

/// <summary>
/// Comment le restaurant traite une commande qui arrive (cahier des charges §3).
/// </summary>
public enum OrderAcceptanceMode
{
    /// <summary>
    /// Le restaurant décide, commande par commande.
    ///
    /// C'est le DÉFAUT, et délibérément. Un maquis qui découvre l'application ne
    /// doit pas se retrouver engagé sur des commandes qu'il n'a pas vues passer —
    /// l'automatique se choisit une fois qu'on a confiance dans son propre rythme.
    /// </summary>
    Manual = 0,

    // `Automatic` CI-DESSOUS EST INATTEIGNABLE (lot 9.2) : aucune route ne
    // permet au restaurateur de basculer son établissement en acceptation
    // automatique. Le mode existe, il est décrit, il est stocké — et il est
    // toujours `Manual`. Ce n'est pas une valeur morte à retirer : c'est une
    // fonction du cahier (§3, §14) déclarée et non construite. Stocké en ENTIER
    // (`HasConversion<int>`) : ne pas renuméroter.

    /// <summary>
    /// Acceptation immédiate : la commande part en cuisine sans attendre personne.
    ///
    /// SUSPENDUE AUTOMATIQUEMENT EN CAS DE SATURATION — le cahier l'exige (§3,
    /// §14). Accepter sans regarder quand la cuisine est déjà pleine, c'est
    /// promettre un délai qu'on ne tiendra pas, et le client l'apprend en
    /// attendant.
    /// </summary>
    Automatic = 1
}

/// <summary>
/// La charge de la cuisine (cahier §14).
///
/// CE N'EST PAS UN STATUT DE RESTAURANT, ET LES CONFONDRE SERAIT UNE ERREUR
/// VISIBLE PAR LE CLIENT.
///
/// Un restaurant saturé n'est pas fermé : il est LENT. Le ranger avec
/// <c>OrderingBlockedReason</c> ferait afficher « revenez demain » à quelqu'un qui
/// aurait très bien pu commander en acceptant vingt minutes de plus.
/// </summary>
public enum KitchenLoadLevel
{
    /// <summary>La cuisine suit.</summary>
    Normal = 0,

    /// <summary>« Forte demande » — le cahier nomme cet affichage (§14). Le délai s'allonge.</summary>
    High = 1,

    /// <summary>Au plafond. L'auto-acceptation se coupe.</summary>
    Saturated = 2
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉTAT DE CHARGE, ET SES QUATRE CONSÉQUENCES (cahier §14).
///
/// Le cahier les énumère : « augmenter l'ETA, désactiver l'auto-acceptation,
/// afficher "forte demande", éventuellement suspendre les nouvelles commandes ».
///
/// Elles sont rendues ENSEMBLE, dans un seul objet, parce qu'elles découlent d'un
/// seul calcul. Les exposer en quatre méthodes séparées aurait laissé un appelant
/// n'en consulter que trois — et c'est toujours la quatrième qui manque.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Level">Ce que le client voit.</param>
/// <param name="ActiveOrders">Commandes en cours au moment du calcul.</param>
/// <param name="ExtraWaitMinutes">
/// À AJOUTER à l'ETA. Dérivé du rythme du restaurant lui-même — voir
/// <see cref="Restaurant.AssessLoad"/> — et non d'une constante globale.
/// </param>
/// <param name="AutoAcceptSuspended">
/// L'auto-acceptation est coupée. Le mode reste « Automatic » en base : c'est
/// une suspension, pas un changement de réglage. Réécrire le mode obligerait
/// quelqu'un à le remettre à la main après le coup de feu, et personne n'y
/// penserait.
/// </param>
/// <param name="BlocksNewOrders">
/// Le restaurant refuse les nouvelles commandes. C'est l'« éventuellement » du
/// cahier : un choix du restaurateur, pas une conséquence automatique.
/// </param>
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
