using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka;

/// <summary>CE QUI PERMET D'EVINCER LES VERDICTS D'UN COMPTE SANS CONNAITRE SES JETONS.</summary>
public sealed class RegistreDeRevocation : IDisposable
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    /// <summary>Le jeton d'expiration a attacher aux entrees de cache de ce compte.</summary>
    public IChangeToken JetonDExpiration(Guid utilisateur)
        => new CancellationChangeToken(_sources.GetOrAdd(utilisateur, _ => new CancellationTokenSource()).Token);

    /// <summary>Evince toutes les entrees de cache de ce compte.</summary>
    public bool Revoquer(Guid utilisateur)
    {
        if (!_sources.TryRemove(utilisateur, out var source))
        {
            return false;
        }

        source.Cancel();
        source.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (var source in _sources.Values)
        {
            source.Dispose();
        }

        _sources.Clear();
    }
}
