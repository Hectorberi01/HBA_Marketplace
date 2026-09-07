using System.Reflection;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Infrastructure.BackgroundJobs;
using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using HBA.Promotions.Domain.Promotions.Events;
using HBA.Promotions.Infrastructure.Persistence;
using HBA.Promotions.Infrastructure.Public;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Promotions.Infrastructure.Caching.Redis;
using HBA.Promotions.Infrastructure.Observability;
using HBA.Promotions.Infrastructure.Idempotency;
namespace HBA.Promotions.Infrastructure;

/// <summary>ENREGISTREMENT DU SERVICE PROMOTION.</summary>
public sealed class PromotionsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Promotions";

    public Assembly ApplicationAssembly => typeof(ValidateCouponQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCachePromotions(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabilitePromotions(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessageriePromotions()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<PromotionsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PromotionsDbContext.SchemaName)));

        services.AddScoped<IPromotionsUnitOfWork>(sp => sp.GetRequiredService<PromotionsDbContext>());
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IPromotionModuleApi, PromotionModuleApi>();

        // Inbox de consommation (§19.5) et idempotence HTTP (§5), dans le schéma du
        // service — voir l'encadré de `PromotionsDbContext`.
        services.AjouterIdempotencePromotions();

        // TROIS LIGNES SANS LESQUELLES LES ÉVÉNEMENTS NE SORTENT PAS.
        services.AddScoped<IDomainEventHandler<PromotionCreatedDomainEvent>, PromotionCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PromotionExhaustedDomainEvent>, PromotionExhaustedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CouponUsedDomainEvent>, CouponUsedDomainEventHandler>();


        // LE BALAYEUR DE BUDGET (ISSUE-053).
        var periode = TimeSpan.FromMinutes(5);
        if (int.TryParse(configuration["Promotions:HoldSweep:IntervalSeconds"], out var secondes)
            && secondes > 0)
        {
            periode = TimeSpan.FromSeconds(secondes);
        }

        var taillePar = 100;
        if (int.TryParse(configuration["Promotions:HoldSweep:BatchSize"], out var lot) && lot > 0)
        {
            taillePar = lot;
        }

        services.AddSingleton(new CouponHoldSweepOptions(periode, taillePar));
        services.AddHostedService<ExpireCouponHoldsWorker>();
    }
}
