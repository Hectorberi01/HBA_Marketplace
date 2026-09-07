using HBA.Shared.Infrastructure.Hosting;
using HBA.Media.Infrastructure.Messaging.Kafka.Configuration;
using System.Reflection;
using FluentValidation;
using HBA.Media.Application.Abstractions;
using HBA.Media.Application.Assets;
using HBA.Media.Contracts;
using HBA.Media.Domain.Assets;
using HBA.Media.Domain.Assets.Events;
using HBA.Media.Infrastructure.ImageProcessing;
using HBA.Media.Infrastructure.ObjectStorage;
using HBA.Media.Infrastructure.Persistence;
using HBA.Media.Infrastructure.Public;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Media.Application.Assets.EventHandlers;
using HBA.Media.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using HBA.Media.Infrastructure.Caching.Redis;
using HBA.Media.Infrastructure.Observability;
namespace HBA.Media.Infrastructure;

/// <summary>ENREGISTREMENT DU SERVICE MÉDIA.</summary>
public sealed class MediaModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Media";

    public Assembly ApplicationAssembly => typeof(UploadMediaCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheMedia(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteMedia(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieMedia()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<MediaDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MediaDbContext.SchemaName)));

        services.AddScoped<IMediaUnitOfWork>(sp => sp.GetRequiredService<MediaDbContext>());
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IMediaModuleApi, MediaModuleApi>();

        // SANS CETTE LIGNE, UN REJEU KAFKA SUPPRIME UN FICHIER DÉJÀ SUPPRIMÉ — ET
        // LE JOURNAL LE DIT EN Debug, DONC PERSONNE NE LE VOIT.

        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));

        // LE CHOIX DU STOCKAGE EST FAIT ICI, ET IL EST BRUYANT.
        var stockage = new ObjectStorageOptions();
        configuration.GetSection(ObjectStorageOptions.SectionName).Bind(stockage);

        if (stockage.IsConfigured)
        {
            services.AddHttpClient<IObjectStorage, S3CompatibleObjectStorage>();
        }
        else
        {
            // EN PRODUCTION, ON REFUSE DE DÉMARRER PLUTÔT QUE DE TOUT PERDRE.
            if (IsProduction(configuration))
            {
                throw new InvalidOperationException(
                    $"Aucun stockage objet configuré en production ({ObjectStorageOptions.SectionName}). "
                    + "Le repli en mémoire perdrait à chaque redémarrage les pièces KYB et les preuves de "
                    + "livraison : le service refuse de démarrer. Renseigner l'endpoint, le bucket et les "
                    + "identifiants S3 (MinIO convient).");
            }

            // Singleton : c'est un dictionnaire en mémoire, et le porter en scoped
            // ferait perdre chaque fichier entre deux requêtes — le substitut
            // deviendrait inutilisable au premier affichage.
            services.AddSingleton<IObjectStorage, InMemoryObjectStorage>();

            services.AddHostedService<UnconfiguredStorageWarning>();
        }

        services.AddScoped<IImageVariantGenerator, SkiaImageVariantGenerator>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // LE FICHIER SURVIVAIT À LA PIÈCE KYB, INDÉFINIMENT.

        // Retirer une pièce effaçait la ligne côté merchant-service et laissait
        // l'objet

        // dans MinIO. Ce n'est pas qu'une question d'espace : une pièce KYB est un

        // document d'identité, gardé après que son propriétaire a demandé son
        // retrait.

        // merchant annonce le FAIT ; media, qui possède le fichier, en tire les

        // conséquences.

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        // TROIS ÉVÉNEMENTS ÉTAIENT LEVÉS ET N'ARRIVAIENT NULLE PART.
        services.AddScoped<IDomainEventHandler<MediaReadyDomainEvent>, MediaReadyDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<MediaDeletedDomainEvent>, MediaDeletedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<MediaProcessingFailedDomainEvent>, MediaProcessingFailedDomainEventHandler>();

    }

    /// <summary>Sommes-nous en production ?</summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        return EnvironnementDeploiement.EstProduction(configuration);
    }
}

/// <summary>Dit AU DÉMARRAGE que le stockage objet n'est pas configuré.</summary>
internal sealed class UnconfiguredStorageWarning : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly Microsoft.Extensions.Logging.ILogger<UnconfiguredStorageWarning> _logger;

    public UnconfiguredStorageWarning(Microsoft.Extensions.Logging.ILogger<UnconfiguredStorageWarning> logger)
        => _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "HBA Media : aucun stockage objet configuré ({Section}). Les fichiers sont conservés EN MÉMOIRE "
            + "et seront perdus au redémarrage. Acceptable en développement, jamais ailleurs.",
            ObjectStorageOptions.SectionName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
