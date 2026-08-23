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
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Media.Application.Assets.EventHandlers;
using HBA.Shared.IntegrationEvents;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HBA.Media.Infrastructure;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ENREGISTREMENT DU SERVICE MÉDIA.
///
/// Fichiers, métadonnées, visibilité, variantes. Le SENS de chaque fichier reste
/// chez son propriétaire — Product, Food, Sellers, Delivery.
///
/// CE MODULE NE CONNAÎT AUCUN AUTRE MODULE, pas même leurs Contracts. C'est la
/// même règle que pour Food, et pour la même raison : le cahier (§2) pose que
/// Media doit pouvoir évoluer « sans modifier les services métier », ce qui
/// suppose d'abord qu'il ne les connaisse pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MediaModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Media";

    public Assembly ApplicationAssembly => typeof(UploadMediaCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<MediaDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MediaDbContext.SchemaName)));

        services.AddScoped<IMediaUnitOfWork>(sp => sp.GetRequiredService<MediaDbContext>());
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IMediaModuleApi, MediaModuleApi>();

        // ═════════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, UN REJEU KAFKA SUPPRIME UN FICHIER DÉJÀ SUPPRIMÉ —
        //    ET LE JOURNAL LE DIT EN Debug, DONC PERSONNE NE LE VOIT.
        //
        // `IntegrationEventDispatcher` résout l'inbox en OPTIONNEL : ce module
        // tournait sans garde, avec un simple avertissement au démarrage du
        // premier message. Le seul consommateur d'ici,
        // `DeleteMediaOnKybDocumentRemovedHandler`, se rattrape à la main — il
        // sort quand le média est absent — mais cette prudence est LA SIENNE :
        // elle disparaît avec le prochain gestionnaire qu'on branchera, et le
        // prochain détruira peut-être des octets sans relire.
        //
        // Le DbContext lié est celui de CE module : la trace part avec le
        // `SaveChangesAsync` du gestionnaire, dans la même transaction que la
        // suppression logique qu'elle protège.
        //
        // CE QUI RESTE DÉCOUVERT ICI. Le retrait des OCTETS dans le stockage
        // objet n'est pas transactionnel : il ne participe pas au SaveChanges. La
        // trace protège la ligne `media_assets`, pas l'appel à MinIO — un rejeu
        // n'en émettra plus, ce qui est déjà l'essentiel, mais un échec entre les
        // deux reste à rattraper par la purge de rétention.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IConsumerInbox, EfConsumerInbox<MediaDbContext>>();

        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));

        // ═════════════════════════════════════════════════════════════════════
        // LE CHOIX DU STOCKAGE EST FAIT ICI, ET IL EST BRUYANT.
        //
        // Sans configuration, on retombe sur un stockage en mémoire — un
        // développeur sans identifiants S3 doit pouvoir lancer l'application et
        // créer un produit. Mais un substitut qui s'installe EN SILENCE, c'est une
        // préproduction qui perd tous ses fichiers à chaque redémarrage pendant
        // trois semaines avant que quelqu'un ne comprenne.
        //
        // D'où l'avertissement au démarrage. Le dépôt fait déjà ce choix ailleurs
        // — SimulatedMediaStorage, SimulatedKybStorage — mais sans le dire.
        // ═════════════════════════════════════════════════════════════════════
        var stockage = new ObjectStorageOptions();
        configuration.GetSection(ObjectStorageOptions.SectionName).Bind(stockage);

        if (stockage.IsConfigured)
        {
            services.AddHttpClient<IObjectStorage, S3CompatibleObjectStorage>();
        }
        else
        {
            // ═════════════════════════════════════════════════════════════════
            // EN PRODUCTION, ON REFUSE DE DÉMARRER PLUTÔT QUE DE TOUT PERDRE.
            //
            // L'avertissement ci-dessous a été écrit pour qu'on ne découvre pas le
            // substitut trois semaines trop tard. Il ne suffit pas : un
            // avertissement de démarrage se lit une fois, le jour du déploiement,
            // et jamais ensuite.
            //
            // Ce que ce module stocke n'est pas seulement des photos de produits :
            // ce sont les pièces KYB — cartes d'identité, registres de commerce —
            // et les preuves de livraison. Les perdre au redémarrage, ce n'est pas
            // une gêne d'affichage, c'est un dossier de conformité qui s'évapore et
            // une preuve qui manque le jour d'un litige.
            //
            // Même règle que les passerelles de paiement et l'e-mail : hors
            // production on simule et on le DIT ; en production on refuse.
            // ═════════════════════════════════════════════════════════════════
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

        //

        // Retirer une pièce effaçait la ligne côté merchant-service et laissait l'objet

        // dans MinIO. Ce n'est pas qu'une question d'espace : une pièce KYB est un

        // document d'identité, gardé après que son propriétaire a demandé son retrait.

        //

        // merchant annonce le FAIT ; media, qui possède le fichier, en tire les

        // conséquences.

        services.AddScoped<IIntegrationEventHandler<KybDocumentRemovedIntegrationEvent>, DeleteMediaOnKybDocumentRemovedHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // TROIS ÉVÉNEMENTS ÉTAIENT LEVÉS ET N'ARRIVAIENT NULLE PART.
        //
        // `MediaAsset` lève « ready », « deleted » et « processing failed »
        // depuis l'origine, et la documentation de ces événements affirmait
        // qu'ils passaient par l'outbox transactionnel. Faute de ces trois
        // lignes, ils étaient dispatchés dans le processus et s'arrêtaient là :
        // rien ne sortait vers Kafka, et le §16 — « les services métier peuvent
        // écouter media.ready pour mettre à jour leur état sans couplage HTTP
        // permanent » — restait une intention.
        //
        // L'inscription est manuelle et rien dans le compilateur ne la rappelle :
        // c'est exactement ainsi que payment-service avait perdu son
        // gestionnaire « paiement initié ».
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IDomainEventHandler<MediaReadyDomainEvent>, MediaReadyDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<MediaDeletedDomainEvent>, MediaDeletedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<MediaProcessingFailedDomainEvent>, MediaProcessingFailedDomainEventHandler>();

        services.AddOutboxProcessor<MediaDbContext>();
    }

    /// <summary>
    /// Sommes-nous en production ?
    ///
    /// Même détection que <c>PaymentsModuleInstaller</c> et
    /// <c>NotificationsModuleInstaller</c> : un <see cref="IModuleInstaller"/> ne
    /// reçoit pas d'<c>IHostEnvironment</c> — les modules s'installent avant que
    /// l'hôte ne soit construit — donc on lit la variable d'environnement standard,
    /// qu'ASP.NET expose dans la configuration.
    ///
    /// L'inconnu est traité comme « pas la production », sinon un environnement
    /// mal nommé refuserait de démarrer en développement.
    /// </summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        var environnement = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? string.Empty;

        return string.Equals(environnement, "Production", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Dit AU DÉMARRAGE que le stockage objet n'est pas configuré.
///
/// Un service hébergé plutôt qu'un log dans l'installer : à ce moment-là, le
/// journal n'existe pas encore. Il tourne une fois, écrit, et s'arrête.
/// </summary>
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
