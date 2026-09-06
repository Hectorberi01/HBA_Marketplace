using HBA.Shared.Application.Abstractions;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Caching`.
//
// Le cache appartient desormais au service : il vit dans son Infrastructure, et
// c'est son propre module qui le branche. `ICacheService` reste en revanche dans
// `HBA.Shared.Application.Abstractions` — c'est le PORT dont depend la couche
// Application, pas l'adaptateur.
//
// `internal` : rien hors de cet assemblage n'a de raison de nommer cette classe.
// Le conteneur la resout par `ICacheService`.
//
// CE QUE ÇA COUTE : cette implementation existe en vingt-six exemplaires. Elles
// sont identiques aujourd'hui, et rien n'empeche qu'elles divergent — un TTL
// change ici ne changera rien ailleurs.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Engagement.Recommendations.Infrastructure.Caching.Redis.Services;

/// <summary>
/// Cache inerte : ne mémorise rien, n'invalide rien, et exécute toujours la source.
///
/// Destiné aux <c>IDesignTimeDbContextFactory</c> (outils EF : migrations add,
/// database update). Ces outils construisent un DbContext À LA MAIN, sans conteneur
/// DI et sans Redis. Exiger un vrai cache là reviendrait à exiger un Redis joignable
/// pour générer une migration — une dépendance absurde entre un outil de schéma et
/// une infrastructure d'exécution.
///
/// Il n'y a d'ailleurs rien à invalider au design-time : le DbContext n'y sert qu'à
/// produire du DDL, sans jamais écrire une ligne de données.
///
/// À ne JAMAIS enregistrer dans le conteneur d'une application. Ce n'est pas un
/// mode « cache désactivé » : c'est un bouchon pour outillage. Pour désactiver le
/// cache à l'exécution, on garde ICacheService et on laisse l'IDistributedCache en
/// mémoire — le comportement reste correct, seul le partage entre instances est perdu.
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
