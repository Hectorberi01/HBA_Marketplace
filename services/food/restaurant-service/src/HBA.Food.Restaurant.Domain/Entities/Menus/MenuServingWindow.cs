using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

/// <summary>QUAND UNE CARTE EST-ELLE SERVIE ? (cahier des charges §5)</summary>
public sealed record MenuServingWindow
{
    // NOMS DE PARAMÈTRES ALIGNÉS SUR LES PROPRIÉTÉS.
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

    /// <summary>
    /// Vrai si aucune restriction n'est posée — utile aux écrans, qui masquent
    /// alors les champs.
    /// </summary>
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
        if (startTime is null != endTime is null)
        {
            return Error.Validation(
                "food.menu.window_incomplete",
                "Un créneau de service demande une heure de début ET une heure de fin.");
        }

        // Début et fin identiques : ni « toute la journée » ni « jamais », et
        // impossible à deviner.
        if (startTime is { } d && endTime is { } f && d == f)
        {
            return Error.Validation(
                "food.menu.window_empty",
                "Début et fin identiques. Laissez les deux vides pour une carte servie toute la journée.");
        }

        return new MenuServingWindow(availableFrom, availableUntil, startTime, endTime);
    }

    /// <summary>Cette carte est-elle servie à cet instant ?</summary>
    public bool IsServedAt(DateTime nowUtc)
    {
        var dateLocale = BeninTime.LocalDate(nowUtc);

        if (AvailableFrom is { } debutPeriode && dateLocale < debutPeriode)
        {
            return false;
        }

        // INCLUS : « jusqu'au 30 septembre » veut dire que le 30 septembre compte.
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
        // se succèdent à 15 h ne doivent pas être servies toutes les deux à 15 h 00
        // pile.
        return WrapsMidnight
            ? heure >= debut || heure < fin
            : heure >= debut && heure < fin;
    }
}
