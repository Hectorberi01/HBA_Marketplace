using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUAND UNE CARTE EST-ELLE SERVIE ? (cahier des charges §5)
///
/// Le cahier donne quatre champs à <c>Menu</c> : <c>AvailableFrom</c>,
/// <c>AvailableUntil</c>, <c>StartTime</c>, <c>EndTime</c>. Ce sont DEUX
/// questions différentes, et les confondre rendrait les deux inutilisables :
///
///   • une PÉRIODE, en dates — « la carte d'été, du 1er juin au 30 septembre » ;
///   • un CRÉNEAU, en heures — « le menu du midi, de 11 h à 15 h, tous les jours ».
///
/// Les quatre champs sont facultatifs. Tout nul = la carte permanente, servie
/// toujours. C'est le cas de loin le plus fréquent, et il ne doit rien coûter à
/// saisir.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE CRÉNEAU-CI PEUT PASSER MINUIT. CELUI DE ServiceHours NON, ET CE N'EST
/// PAS UNE INCOHÉRENCE.
///
/// <c>ServiceHours.Create</c> refuse « 19 h – 02 h » parce qu'un créneau de
/// service appartient à un JOUR de la semaine : la seconde moitié serait-elle du
/// vendredi ou du samedi ? La question n'a pas de réponse, alors le type refuse
/// de la poser.
///
/// Un créneau de carte n'a pas de jour. « Le menu de nuit, de 22 h à 2 h » se lit
/// sans ambiguïté : entre 22 h et minuit, OU entre minuit et 2 h. Rien à
/// attribuer, donc rien à trancher.
///
/// Interdire l'enroulement ici par simple souci de symétrie aurait forcé le
/// maquis ouvert tard à saisir deux cartes identiques — et à les modifier toutes
/// les deux à chaque changement de prix.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MenuServingWindow
{
    // NOMS DE PARAMÈTRES ALIGNÉS SUR LES PROPRIÉTÉS.
    //
    // EF matérialise un type sans constructeur public en liant les PARAMÈTRES aux
    // propriétés PAR LEUR NOM. Un décalage ne se voit qu'à la première lecture en
    // base, jamais à la compilation. Le module s'est déjà fait prendre une fois,
    // sur ItemAvailability.
    private MenuServingWindow(
        DateOnly? availableFrom, DateOnly? availableUntil, TimeOnly? startTime, TimeOnly? endTime)
    {
        AvailableFrom = availableFrom;
        AvailableUntil = availableUntil;
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>Premier jour de validité, en date LOCALE. Nul = depuis toujours.</summary>
    public DateOnly? AvailableFrom { get; }

    /// <summary>Dernier jour de validité, INCLUS. Nul = sans fin.</summary>
    public DateOnly? AvailableUntil { get; }

    /// <summary>Début du créneau, en heure LOCALE. Nul = toute la journée.</summary>
    public TimeOnly? StartTime { get; }

    /// <summary>Fin du créneau, EXCLUE. Nul = toute la journée.</summary>
    public TimeOnly? EndTime { get; }

    /// <summary>La carte permanente : servie tous les jours, à toute heure.</summary>
    public static MenuServingWindow Always { get; } = new(null, null, null, null);

    /// <summary>Vrai si aucune restriction n'est posée — utile aux écrans, qui masquent alors les champs.</summary>
    public bool IsAlways
        => AvailableFrom is null && AvailableUntil is null && StartTime is null && EndTime is null;

    /// <summary>Le créneau horaire passe-t-il minuit ? « 22 h – 2 h ».</summary>
    public bool WrapsMidnight => StartTime is { } debut && EndTime is { } fin && fin <= debut;

    public static Result<MenuServingWindow> Create(
        DateOnly? availableFrom, DateOnly? availableUntil, TimeOnly? startTime, TimeOnly? endTime)
    {
        if (availableFrom is { } debut && availableUntil is { } fin && fin < debut)
        {
            return Error.Validation(
                "food.menu.period_invalid",
                "La fin de validité de la carte doit suivre son début.");
        }

        // LES DEUX HEURES VONT ENSEMBLE, OU AUCUNE.
        //
        // « À partir de 11 h » sans fin, ou « jusqu'à 15 h » sans début, laisse la
        // moitié de la question sans réponse : servie jusqu'à quand ? depuis
        // quand ? Chaque lecture aurait dû inventer un défaut, et deux écrans
        // auraient inventé le même différemment.
        if (startTime is null != endTime is null)
        {
            return Error.Validation(
                "food.menu.window_incomplete",
                "Un créneau de service demande une heure de début ET une heure de fin.");
        }

        // Début et fin identiques : ni « toute la journée » ni « jamais », et
        // impossible à deviner. On refuse plutôt que de choisir à la place du
        // restaurateur — qui laissera les deux champs vides s'il veut la journée
        // entière.
        if (startTime is { } d && endTime is { } f && d == f)
        {
            return Error.Validation(
                "food.menu.window_empty",
                "Début et fin identiques. Laissez les deux vides pour une carte servie toute la journée.");
        }

        return new MenuServingWindow(availableFrom, availableUntil, startTime, endTime);
    }

    /// <summary>
    /// Cette carte est-elle servie à cet instant ?
    ///
    /// L'INSTANT EST UTC, LA COMPARAISON EST LOCALE. C'est tout l'enjeu :
    /// « 11 h – 15 h » est saisi par un restaurateur à Cotonou, et comparé à un
    /// UTC brut il décalerait le menu du midi d'une heure — servi de 10 h à 14 h,
    /// donc absent au moment précis où l'on déjeune.
    /// </summary>
    public bool IsServedAt(DateTime nowUtc)
    {
        var dateLocale = BeninTime.LocalDate(nowUtc);

        if (AvailableFrom is { } debutPeriode && dateLocale < debutPeriode)
        {
            return false;
        }

        // INCLUS : « jusqu'au 30 septembre » veut dire que le 30 septembre compte.
        // Exclure ce jour retirerait la carte d'été vingt-quatre heures trop tôt,
        // et personne ne relierait l'absence de commandes à cette borne.
        if (AvailableUntil is { } finPeriode && dateLocale > finPeriode)
        {
            return false;
        }

        if (StartTime is not { } debut || EndTime is not { } fin)
        {
            return true;
        }

        var heure = BeninTime.LocalTimeOfDay(nowUtc);

        // La borne de fin est EXCLUE, comme celle de ServiceHours : deux cartes qui
        // se succèdent à 15 h ne doivent pas être servies toutes les deux à
        // 15 h 00 pile.
        return WrapsMidnight
            ? heure >= debut || heure < fin
            : heure >= debut && heure < fin;
    }
}
