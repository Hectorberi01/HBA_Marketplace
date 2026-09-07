namespace HBA.Shared.Infrastructure.Events;

/// <summary>Le drainage de l'outbox est-il actif sur cet hote ?</summary>
public static class DrainageDOutbox
{
    public static bool Actif =>
        !string.Equals(Environment.GetEnvironmentVariable("OUTBOX_ENABLED"), "false",
                       StringComparison.OrdinalIgnoreCase);
}
