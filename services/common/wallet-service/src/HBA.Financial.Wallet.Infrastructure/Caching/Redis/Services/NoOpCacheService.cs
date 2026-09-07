using HBA.Shared.Application.Abstractions;

// COPIE DEPUIS `HBA.Shared.Infrastructure.Caching`.

namespace HBA.Financial.Wallet.Infrastructure.Caching.Redis.Services;

/// <summary>
/// Cache inerte : ne mémorise rien, n'invalide rien, et exécute toujours la source.
/// </summary>
internal sealed class NoOpCacheService : ICacheService
{
    public static readonly NoOpCacheService Instance = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null,
        TimeSpan? missTtl = null,
        CancellationToken cancellationToken = default)
        where T : class
        => factory(cancellationToken);
}
