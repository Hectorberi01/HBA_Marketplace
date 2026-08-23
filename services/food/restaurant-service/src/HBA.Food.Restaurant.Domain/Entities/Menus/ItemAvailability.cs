using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA DISPONIBILITÉ D'UN ARTICLE OU D'UNE OPTION.
///
/// REMPLACE UN BOOLÉEN QUI NE REVENAIT JAMAIS À VRAI.
///
/// La première version portait un simple <c>IsAvailable</c>. Le cuisinier
/// n'ayant plus de poisson le décochait à midi — et devait penser à le recocher
/// le lendemain matin, avant le service. Il ne le fait pas. Le plat disparaît de
/// la carte pour des semaines, et personne ne relie l'absence de commandes à une
/// case décochée un mardi.
///
/// C'est exactement le motif que ce dépôt traque ailleurs : un état qu'on pose et
/// que rien ne relève.
///
/// DEUX INTENTIONS DIFFÉRENTES, ET ELLES NE SE CONFONDENT PAS
///
///   • « plus de poisson AUJOURD'HUI » — revient tout seul au service suivant ;
///   • « on ne fait plus ce plat » — reste absent jusqu'à décision contraire.
///
/// Les fondre obligerait le restaurateur à choisir entre oublier de remettre son
/// plat, ou voir revenir un plat qu'il a retiré.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ItemAvailability
{
    // LES NOMS DE PARAMÈTRES DOIVENT REFLÉTER CEUX DES PROPRIÉTÉS.
    //
    // EF Core matérialise un type sans constructeur public en liant les
    // PARAMÈTRES aux propriétés PAR LEUR NOM. Un « available » face à un
    // « IsMarkedAvailable » ne se lie pas, et l'échec ne survient qu'à la
    // première lecture en base — pas à la compilation.
    private ItemAvailability(bool isMarkedAvailable, DateTime? unavailableUntilUtc)
    {
        IsMarkedAvailable = isMarkedAvailable;
        UnavailableUntilUtc = unavailableUntilUtc;
    }

    /// <summary>
    /// L'intention brute du restaurateur, SANS tenir compte de l'heure.
    ///
    /// NE PAS L'UTILISER POUR DÉCIDER D'UNE VENTE — un article épuisé jusqu'à
    /// ce soir la porte à faux jusqu'à l'échéance. La question qui compte est
    /// <see cref="IsAvailableAt"/>.
    /// </summary>
    public bool IsMarkedAvailable { get; }

    /// <summary>
    /// Instant de retour automatique. Nul quand l'indisponibilité est durable —
    /// et c'est cette nullité qui distingue les deux intentions.
    /// </summary>
    public DateTime? UnavailableUntilUtc { get; }

    /// <summary>Disponible.</summary>
    public static ItemAvailability Available() => new(true, null);

    /// <summary>
    /// Épuisé jusqu'à un instant donné : revient tout seul.
    ///
    /// L'instant est CALCULÉ PAR L'APPELANT — typiquement la fin du service du
    /// jour, que seul le restaurant connaît (voir Restaurant.EndOfServiceDayUtc).
    /// Ce type ne connaît pas les horaires ; il ne fait que tenir la promesse.
    /// </summary>
    public static Result<ItemAvailability> UntilUtc(DateTime untilUtc, DateTime nowUtc)
    {
        if (untilUtc <= nowUtc)
        {
            // Une échéance déjà passée rendrait l'article disponible à l'instant
            // même où on le déclare épuisé — le restaurateur croirait l'avoir
            // retiré et les commandes continueraient.
            return Error.Validation(
                "food.availability.until_in_past", "L'échéance de retour doit être dans le futur.");
        }

        return new ItemAvailability(false, untilUtc);
    }

    /// <summary>Retiré durablement : ne revient que sur décision.</summary>
    public static ItemAvailability Indefinitely() => new(false, null);

    /// <summary>
    /// Est-ce vendable à cet instant ?
    ///
    /// L'HEURE EST PASSÉE, JAMAIS LUE ICI : un domaine qui appelle
    /// <c>DateTime.UtcNow</c> ne se teste qu'en attendant l'heure qui l'intéresse.
    /// </summary>
    public bool IsAvailableAt(DateTime nowUtc)
    {
        if (IsMarkedAvailable)
        {
            return true;
        }

        // Échéance dépassée : l'article est revenu de lui-même. On ne réécrit pas
        // l'état pour autant — une LECTURE qui mute est une lecture qu'on n'ose
        // plus appeler deux fois. La remise à zéro effective se fait à la
        // prochaine écriture, ou jamais : le résultat est le même.
        return UnavailableUntilUtc is { } echeance && echeance <= nowUtc;
    }

    /// <summary>Indisponibilité DURABLE, qui ne se lèvera pas d'elle-même.</summary>
    public bool IsIndefinitelyUnavailable => !IsMarkedAvailable && UnavailableUntilUtc is null;
}
