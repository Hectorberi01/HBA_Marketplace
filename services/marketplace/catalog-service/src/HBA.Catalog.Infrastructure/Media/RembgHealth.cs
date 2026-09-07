namespace HBA.Catalog.Infrastructure.Media;

/// <summary>Santé observée du service de détourage local, partagée par le processus.</summary>
public sealed class RembgHealth
{
    private const int CooldownMinutes = 2;

    // `long` et Interlocked plutôt qu'un DateTime verrouillé : l'écriture vient de
    // n'importe quelle requête, la lecture de n'importe quelle autre.
    private long _lastFailureTicks;

    public void MarkSuccess() => Interlocked.Exchange(ref _lastFailureTicks, 0);

    public void MarkFailure() => Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);

    public bool IsHealthy
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFailureTicks);
            if (ticks == 0)
            {
                return true;
            }

            return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)
                   > TimeSpan.FromMinutes(CooldownMinutes);
        }
    }
}
