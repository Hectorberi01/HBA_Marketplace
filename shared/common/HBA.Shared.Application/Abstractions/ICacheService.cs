namespace HBA.Shared.Application.Abstractions;

/// <summary>Cache distribué applicatif (Redis en production, mémoire en repli).</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache-aside en un appel : renvoie la valeur en cache, sinon exécute
    /// <paramref name="factory"/> (la lecture en base) et mémorise son résultat.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        TimeSpan? missTtl = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Supprime plusieurs clés (invalidation d'une écriture qui touche plusieurs
    /// vues).
    /// </summary>
    Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}
