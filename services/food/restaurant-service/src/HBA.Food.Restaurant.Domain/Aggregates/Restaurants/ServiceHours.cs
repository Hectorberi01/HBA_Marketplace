using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Restaurants;

/// <summary>Un créneau de service d'un jour de la semaine.</summary>
public sealed record ServiceHours
{
    private ServiceHours(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        Day = day;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public DayOfWeek Day { get; }
    public TimeOnly OpensAt { get; }
    public TimeOnly ClosesAt { get; }

    public static Result<ServiceHours> Create(DayOfWeek day, TimeOnly opensAt, TimeOnly closesAt)
    {
        if (closesAt <= opensAt)
        {
            // PAS DE CRÉNEAU À CHEVAL SUR MINUIT — et la restauration est
            // précisément le métier où l'on serait tenté de l'autoriser.
            return Error.Validation(
                "food.hours.invalid",
                "L'heure de fermeture doit être postérieure à l'heure d'ouverture. "
                + "Un service qui passe minuit se saisit en deux créneaux, sur deux jours.");
        }

        return new ServiceHours(day, opensAt, closesAt);
    }

    /// <summary>Ce créneau couvre-t-il cet instant ?</summary>
    public bool Covers(DayOfWeek day, TimeOnly time)
        => Day == day && time >= OpensAt && time < ClosesAt;

    /// <summary>Ce créneau en recouvre-t-il un autre du même jour ?</summary>
    public bool Overlaps(ServiceHours other)
        => Day == other.Day && OpensAt < other.ClosesAt && other.OpensAt < ClosesAt;
}
