using HBA.Shared.Domain.Results;

namespace HBA.Analytics.Application.RollUps.Queries;

/// <summary>
/// La période d'un graphe : bornes vérifiées une seule fois, pour les trois
/// requêtes.
/// </summary>
public sealed record PeriodeDemandee(DateOnly Du, DateOnly Au)
{
    /// <summary>Nombre maximal de journées rendues en une requête.</summary>
    public const int JoursMax = 366;

    /// <summary>Fenêtre par défaut quand l'appelant ne dit rien : le mois écoulé.</summary>
    public const int JoursParDefaut = 30;

    /// <summary>Nombre de journées, bornes incluses.</summary>
    public int Jours => Au.DayNumber - Du.DayNumber + 1;

    /// <summary>Construit la période, ou dit pourquoi elle est refusée.</summary>
    public static Result<PeriodeDemandee> Construire(DateOnly? du, DateOnly? au)
    {
        var fin = au ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var debut = du ?? fin.AddDays(-(JoursParDefaut - 1));

        if (fin < debut)
        {
            return Error.Validation(
                ErrorCodes.ValidationError,
                "La borne « to » précède la borne « from ».");
        }

        var jours = fin.DayNumber - debut.DayNumber + 1;

        if (jours > JoursMax)
        {
            return Error.Validation(
                ErrorCodes.ValidationError,
                $"La période demandée couvre {jours} journées ; le maximum est {JoursMax}.");
        }

        return new PeriodeDemandee(debut, fin);
    }

    /// <summary>Toutes les journées de la période, sans trou.</summary>
    public IEnumerable<DateOnly> Journees()
    {
        for (var jour = Du; jour <= Au; jour = jour.AddDays(1))
        {
            yield return jour;
        }
    }
}
