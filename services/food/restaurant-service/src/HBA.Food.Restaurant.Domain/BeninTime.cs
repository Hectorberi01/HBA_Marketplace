namespace HBA.Food.Domain;

/// <summary>L'HEURE LOCALE DU BÉNIN, EN UN SEUL ENDROIT.</summary>
public static class BeninTime
{
    /// <summary>UTC+1, sans heure d'été.</summary>
    public const int UtcOffsetHours = 1;

    /// <summary>L'instant UTC, vu depuis Cotonou.</summary>
    public static DateTime ToLocal(DateTime utc) => utc.AddHours(UtcOffsetHours);

    /// <summary>L'inverse, pour rendre une échéance calculée en local.</summary>
    public static DateTime ToUtc(DateTime local) => local.AddHours(-UtcOffsetHours);

    /// <summary>L'heure du jour, en local.</summary>
    public static TimeOnly LocalTimeOfDay(DateTime utc) => TimeOnly.FromDateTime(ToLocal(utc));

    /// <summary>La date locale. Un service de 23 h appartient encore à la veille en UTC.</summary>
    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));
}
