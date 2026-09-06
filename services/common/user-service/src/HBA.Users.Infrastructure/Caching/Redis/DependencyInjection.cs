using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HBA.Users.Infrastructure.Caching.Redis.Services;

namespace HBA.Users.Infrastructure.Caching.Redis;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CACHE DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// Il etait branche par `AddBuildingBlocksInfrastructure`, donc pour les
/// vingt-six services a la fois. Il l'est desormais par le service lui-meme.
///
/// REDIS ABSENT : ON RETOMBE EN MEMOIRE, MAIS BRUYAMMENT. Le repli reste possible
/// — un poste de developpement n'a pas toujours un Redis. Ce qu'on ne refait pas,
/// c'est le repli SILENCIEUX, celui qui se decouvre en production : le message
/// part sur la sortie standard, le conteneur n'etant pas encore construit.
///
/// UN PREFIXE COMMUN, ET NON UN PAR SERVICE. Les services partagent une instance
/// Redis. Le prefixe les isole d'un autre locataire, pas les uns des autres : les
/// cles sont deja nommees par domaine, et deux repliques du MEME service doivent
/// imperativement partager la leur.
///
/// LES GARDES `Try*` NE SONT PAS DU CONFORT. Trois hotes montent plusieurs
/// modules dans un seul processus — `HBA.Financial.Api` en monte trois. Sans
/// elles, `IDistributedCache` serait enregistre trois fois et le dernier
/// gagnerait : sans effet ici, mais ce serait exactement la forme d'un vrai
/// doublon qu'on ne verrait plus.
///
/// CE QUE ÇA NE COUVRE PAS : la chaine de connexion reste lue dans
/// « Redis:ConnectionString », donc dans la configuration du deploiement. Un
/// service ne choisit pas SON Redis — il choisit ce qu'il en fait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterCacheUsers(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (services.All(d => d.ServiceType != typeof(IDistributedCache)))
        {
            var redis = configuration["Redis:ConnectionString"];

            if (string.IsNullOrWhiteSpace(redis))
            {
                Console.WriteLine(
                    "[HBA] Redis absent — cache EN MEMOIRE, par instance. "
                    + "Toute invalidation ne touchera que le processus qui l'a declenchee : "
                    + "les autres repliques serviront des valeurs perimees jusqu'au TTL. "
                    + "Renseignez « Redis:ConnectionString » hors developpement.");

                services.AddDistributedMemoryCache();
            }
            else
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redis;
                    options.InstanceName = "hba:";
                });
            }
        }

        // LE LOGGER EST RESOLU EN OPTIONNEL, ET C'EST DELIBERE.
        //
        // Le cache journalise ses pannes (Redis injoignable, invalidation ratee).
        // Exiger un `ILogger` ferait echouer tout conteneur monte sans
        // `AddLogging()` — c'est le cas de plusieurs harnais de tests, qui
        // n'installent qu'un module. Le cache aurait alors casse des tests qui
        // n'ont rien a voir avec lui.
        services.TryAddSingleton<ICacheService>(sp => new DistributedCacheService(
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetService<ILogger<DistributedCacheService>>() ?? NullLogger<DistributedCacheService>.Instance));

        return services;
    }
}
