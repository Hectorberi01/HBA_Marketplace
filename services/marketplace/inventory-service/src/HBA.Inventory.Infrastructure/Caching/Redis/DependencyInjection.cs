using HBA.Shared.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HBA.Inventory.Infrastructure.Caching.Redis.Services;

namespace HBA.Inventory.Infrastructure.Caching.Redis;

/// <summary>LE CACHE DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterCacheInventory(
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
        services.TryAddSingleton<ICacheService>(sp => new DistributedCacheService(
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetService<ILogger<DistributedCacheService>>() ?? NullLogger<DistributedCacheService>.Instance));

        return services;
    }
}
