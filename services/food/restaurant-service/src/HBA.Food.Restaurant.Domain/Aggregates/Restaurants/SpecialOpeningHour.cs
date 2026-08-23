using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Restaurants;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE EXCEPTION D'HORAIRE, À UNE DATE PRÉCISE (cahier des charges §4).
///
/// « 15 août → Fermé ». « 31 décembre → 18 h – 2 h ». Fête de la Tabaski, jour de
/// l'Indépendance, inventaire, mariage du patron.
///
/// POURQUOI CE TYPE EXISTE PLUTÔT QUE « MODIFIER SES HORAIRES CE JOUR-LÀ »
///
/// Sans lui, fermer le 1er août oblige à supprimer le créneau du vendredi, puis à
/// le ressaisir le 2. La seconde moitié du geste ne se fait jamais : personne ne
/// pense, le samedi matin, à remettre des horaires qu'il a effacés la veille. Le
/// restaurant reste fermé une semaine, et l'absence de commandes ne se relie à
/// rien.
///
/// Une exception est DATÉE : elle expire d'elle-même. C'est le même raisonnement
/// que la disponibilité datée d'un article, et que la pause bornée du restaurant —
/// ce dépôt traque partout les états qu'on pose et que rien ne relève.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SpecialOpeningHour
{
    // NOMS DE PARAMÈTRES ALIGNÉS SUR LES PROPRIÉTÉS : EF lie par NOM les types
    // sans constructeur public, et l'échec ne surviendrait qu'à la première
    // lecture en base. Le module s'est déjà fait prendre sur ItemAvailability.
    private SpecialOpeningHour(DateOnly date, bool isClosed, TimeOnly? opensAt, TimeOnly? closesAt, string? reason)
    {
        Date = date;
        IsClosed = isClosed;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        Reason = reason;
    }

    /// <summary>Le jour concerné, en date LOCALE du Bénin.</summary>
    public DateOnly Date { get; }

    /// <summary>Fermé toute la journée. Les heures sont alors nulles.</summary>
    public bool IsClosed { get; }

    public TimeOnly? OpensAt { get; }
    public TimeOnly? ClosesAt { get; }

    /// <summary>« Fête de l'Indépendance », « inventaire ». Affiché au client.</summary>
    public string? Reason { get; }

    /// <summary>Fermeture exceptionnelle : le cas de loin le plus fréquent.</summary>
    public static Result<SpecialOpeningHour> Closed(DateOnly date, string? reason)
        => new SpecialOpeningHour(date, isClosed: true, null, null, Clean(reason));

    /// <summary>
    /// Horaires exceptionnels, qui REMPLACENT ceux du jour.
    ///
    /// UN SEUL CRÉNEAU, contrairement aux horaires hebdomadaires qui en
    /// acceptent plusieurs. Un jour exceptionnel est un jour où l'on fait
    /// simple — et permettre deux créneaux aurait demandé de gérer leur
    /// chevauchement pour un cas que personne ne rencontre.
    /// </summary>
    public static Result<SpecialOpeningHour> Open(
        DateOnly date, TimeOnly opensAt, TimeOnly closesAt, string? reason)
    {
        if (closesAt <= opensAt)
        {
            // Même refus que ServiceHours, et pour la même raison : un créneau
            // attaché à un JOUR ne peut pas passer minuit sans qu'on sache à quel
            // jour appartient sa seconde moitié.
            return Error.Validation(
                "food.special_hours.invalid",
                "L'heure de fermeture doit être postérieure à l'heure d'ouverture.");
        }

        return new SpecialOpeningHour(date, isClosed: false, opensAt, closesAt, Clean(reason));
    }

    /// <summary>Cette exception couvre-t-elle cet instant local ?</summary>
    public bool Covers(TimeOnly time)
        => !IsClosed && OpensAt is { } debut && ClosesAt is { } fin && time >= debut && time < fin;

    private static string? Clean(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
