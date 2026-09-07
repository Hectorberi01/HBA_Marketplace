using System.Reflection;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Application.Brands.EventHandlers;
using HBA.Catalog.Application.Categories.EventHandlers;
using HBA.Catalog.Application.Products.Commands.CreateProduct;
using HBA.Catalog.Application.Products.EventHandlers;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Brands;
using HBA.Catalog.Domain.Brands.Events;
using HBA.Catalog.Domain.Categories;
using HBA.Catalog.Domain.Categories.Events;
using HBA.Shared.Infrastructure.Configuration;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Domain.Reviews;
using HBA.Catalog.Domain.Products.Events;
using HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Catalog.Infrastructure.Media;
using HBA.Catalog.Infrastructure.Persistence;
using HBA.Catalog.Infrastructure.Public;

using HBA.Catalog.Infrastructure.Caching.Redis;
using HBA.Catalog.Infrastructure.Observability;
using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Catalog.Infrastructure.Idempotency;
namespace HBA.Catalog.Infrastructure;

/// <summary>
/// Enregistre tout le module Catalog : DbContext (schéma propre), repositories, API
/// publique, handlers d'events, validators, processeur d'outbox.
/// </summary>
public sealed class CatalogModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Catalog";

    public Assembly ApplicationAssembly => typeof(CreateProductCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheCatalog(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteCatalog(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieCatalog()`, que le composition root peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName)));

        // Unit of Work propre au module (interface dédiée -> pas de collision DI).
        services.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        // Ports du module.
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductOfferRepository, ProductOfferRepository>();

        // Le journal des décisions d'administration (§16).
        services.AddScoped<IProductReviewRepository, ProductReviewRepository>();

        // Le référentiel d'attributs et les demandes de marque (§10).
        services.AddScoped<IAttributeDefinitionRepository, AttributeDefinitionRepository>();
        services.AddScoped<ICategoryAttributeRepository, CategoryAttributeRepository>();
        services.AddScoped<IBrandRequestRepository, BrandRequestRepository>();

        // INBOX DE CONSOMMATION (§19.5) ET IDEMPOTENCE DES ÉCRITURES (§25).
        services.AjouterIdempotenceCatalog();

        // LE BARÈME DES OFFRES — SOURCE UNIQUE, VALIDÉE AU DÉMARRAGE.
        var bareme = new PlatformPricing(configuration);
        services.AddSingleton(bareme);
        services.AddSingleton<IOfferPricingSettings>(new OfferPricingSettings(bareme));
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICatalogModuleApi, CatalogModuleApi>();

        // INTERFACE DISTINCTE, PAS UNE MÉTHODE DE PLUS SUR `ICatalogModuleApi`.
        services.AddScoped<IOfferModuleApi, OfferModuleApi>();

        // AUCUN STOCKAGE ICI, ET C'EST LE POINT DE CETTE BASCULE.

        // TRAITEMENT D'IMAGE : UN CHOIX AU DÉMARRAGE, PAS UN REPLI À CHAUD.
        var rembg = BindRembgOptions(configuration);
        var cloudinary = BindCloudinaryOptions(configuration);
        services.AddSingleton(rembg);
        services.AddSingleton(cloudinary);

        if (rembg.IsConfigured)
        {
            services.AddHttpClient(RembgImageProcessor.ClientName, client =>
            {
                // Le délai est porté par un CancellationToken dans l'adaptateur,
                // afin de distinguer « trop lent » d'« annulé par l'appelant ».
                client.Timeout = TimeSpan.FromSeconds(Math.Max(30, rembg.TimeoutSeconds) + 30);
            });
            // Santé partagée par tout le processus : une panne constatée par une
            // requête doit être connue de la suivante, et de l'endpoint de
            // capacités.
            services.AddSingleton<RembgHealth>();
            services.AddScoped<IImageProcessor, RembgImageProcessor>();
        }
        else if (cloudinary.IsConfigured)
        {
            services.AddHttpClient(CloudinaryImageProcessor.ClientName);
            services.AddScoped<IImageProcessor, CloudinaryImageProcessor>();
        }
        else
        {
            services.AddScoped<IImageProcessor, NullImageProcessor>();
        }

        // LE MARQUEUR DE DISPONIBILITÉ SE RÉSOUT PAR RENVOI, ET IL MANQUAIT.
        services.AddScoped<IImageProcessingAvailability>(sp =>
            (IImageProcessingAvailability)sp.GetRequiredService<IImageProcessor>());

        // Handlers de domain events (résolus par le DomainEventDispatcher).
        services.AddScoped<IDomainEventHandler<ProductCreatedDomainEvent>, ProductCreatedDomainEventHandler>();
        // HUIT ENREGISTREMENTS LÀ OÙ IL Y EN AVAIT UN — ET CHACUN COMPTE.
        services.AddScoped<IDomainEventHandler<ProductSubmittedForReviewDomainEvent>, ProductSubmittedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductApprovedDomainEvent>, ProductApprovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductRejectedDomainEvent>, ProductRejectedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductPublishedDomainEvent>, ProductPublishedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductUnpublishedDomainEvent>, ProductUnpublishedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductSuspendedDomainEvent>, ProductSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductRestoredDomainEvent>, ProductRestoredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductArchivedDomainEvent>, ProductArchivedDomainEventHandler>();

        // Les deux événements de marque du §19 (lot 4).
        services.AddScoped<IDomainEventHandler<BrandRequestedDomainEvent>, BrandRequestedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<BrandRequestApprovedDomainEvent>, BrandRequestApprovedDomainEventHandler>();

        // SANS CET ENREGISTREMENT, DÉTACHER UNE IMAGE NE SUPPRIME RIEN.
        services.AddScoped<IDomainEventHandler<ProductMediaRemovedDomainEvent>, ProductMediaRemovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<BrandCreatedDomainEvent>, BrandCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CategoryCreatedDomainEvent>, CategoryCreatedDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        // Validators FluentValidation du module.
        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // Processeur d'outbox dédié au DbContext du module.
    }

    /// <summary>
    /// Lie « Media:Rembg ». Absence de section = fonction inactive, jamais d'erreur
    /// au démarrage : une installation sans détourage doit rester une installation
    /// qui démarre.
    /// </summary>
    private static RembgOptions BindRembgOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Media:Rembg");
        var options = new RembgOptions
        {
            BaseUrl = section["BaseUrl"] ?? string.Empty,
        };

        var model = section["Model"];
        if (!string.IsNullOrWhiteSpace(model))
        {
            options.Model = model.Trim();
        }

        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0)
        {
            options.TimeoutSeconds = timeout;
        }

        if (int.TryParse(section["JpegQuality"], out var quality) && quality is > 0 and <= 100)
        {
            options.JpegQuality = quality;
        }

        return options;
    }

    private static CloudinaryOptions BindCloudinaryOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Media:Cloudinary");
        var options = new CloudinaryOptions
        {
            CloudName = section["CloudName"] ?? string.Empty,
            ApiKey = section["ApiKey"] ?? string.Empty,
            ApiSecret = section["ApiSecret"] ?? string.Empty,
        };
        if (int.TryParse(section["MaxWaitSeconds"], out var wait) && wait > 0)
        {
            options.MaxWaitSeconds = wait;
        }
        return options;
    }
}
