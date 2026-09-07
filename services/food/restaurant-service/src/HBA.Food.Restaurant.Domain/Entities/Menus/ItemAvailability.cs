using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Menus;

/// <summary>LA DISPONIBILITÉ D'UN ARTICLE OU D'UNE OPTION.</summary>
public sealed record ItemAvailability
{
    // LES NOMS DE PARAMÈTRES DOIVENT REFLÉTER CEUX DES PROPRIÉTÉS.
    private ItemAvailability(bool isMarkedAvailable, DateTime? unavailableUntilUtc)
    {
        IsMarkedAvailable = isMarkedAvailable;
        UnavailableUntilUtc = unavailableUntilUtc;
    }

    /// <summary>L'intention brute du restaurateur, SANS tenir compte de l'heure.</summary>
    public bool IsMarkedAvailable { get; }

    /// <summary>Instant de retour automatique.</summary>
    public DateTime? UnavailableUntilUtc { get; }

    /// <summary>Disponible.</summary>
    public static ItemAvailability Available() => new(true, null);

    /// <summary>Épuisé jusqu'à un instant donné : revient tout seul.</summary>
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

    /// <summary>Est-ce vendable à cet instant ?</summary>
    public bool IsAvailableAt(DateTime nowUtc)
    {
        if (IsMarkedAvailable)
        {
            return true;
        }

        // Échéance dépassée : l'article est revenu de lui-même.
        return UnavailableUntilUtc is { } echeance && echeance <= nowUtc;
    }

    /// <summary>Indisponibilité DURABLE, qui ne se lèvera pas d'elle-même.</summary>
    public bool IsIndefinitelyUnavailable => !IsMarkedAvailable && UnavailableUntilUtc is null;
}
