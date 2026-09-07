namespace HBA.Analytics.Domain.RollUps;

/// <summary>La conversion « instant → journée » de tout ce service, en un seul endroit.</summary>
public static class JourneeAnalytique
{
    /// <summary>La journée UTC qui contient cet instant.</summary>
    public static DateOnly De(DateTime instantUtc)
        => DateOnly.FromDateTime(instantUtc.Kind == DateTimeKind.Utc
            ? instantUtc
            : instantUtc.ToUniversalTime());
}
