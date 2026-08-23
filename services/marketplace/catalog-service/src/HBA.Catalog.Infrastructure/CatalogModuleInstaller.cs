using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
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
using HBA.Catalog.Infrastructure.Integration;
using HBA.Catalog.Infrastructure.Media;
using HBA.Catalog.Infrastructure.Persistence;
using HBA.Catalog.Infrastructure.Public;

namespace HBA.Catalog.Infrastructure;

/// <summary>
/// Enregistre tout le module Catalog : DbContext (schéma propre), repositories,
/// API publique, handlers d'events, validators, processeur d'outbox. Le
/// Bootstrap se contente d'appeler cet installer — il ne connaît pas les
/// internes du module.
/// </summary>
public sealed class CatalogModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Catalog";

    public Assembly ApplicationAssembly => typeof(CreateProductCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName)));

        // Unit of Work propre au module (interface dédiée -> pas de collision DI).
        services.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        // Ports du module.
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductOfferRepository, ProductOfferRepository>();

        // Le journal des décisions d'administration (§16). Sans cet enregistrement,
        // `AdminReviewCommandHandler` ne se construit pas et les quatre routes
        // d'administration rendent 500 — MediatR ne résout pas le handler.
        services.AddScoped<IProductReviewRepository, ProductReviewRepository>();

        // Le référentiel d'attributs et les demandes de marque (§10).
        services.AddScoped<IAttributeDefinitionRepository, AttributeDefinitionRepository>();
        services.AddScoped<ICategoryAttributeRepository, CategoryAttributeRepository>();
        services.AddScoped<IBrandRequestRepository, BrandRequestRepository>();

        // ═════════════════════════════════════════════════════════════════════
        // INBOX DE CONSOMMATION (§19.5) ET IDEMPOTENCE DES ÉCRITURES (§25).
        //
        // LES DEUX TABLES EXISTENT SANS CES DEUX LIGNES, ET NE SERVENT À RIEN.
        //
        // `CatalogDbContext` applique désormais leurs configurations : les tables
        // seront créées par la prochaine migration. Mais `IConsumerInbox` non
        // enregistré, un gestionnaire qui l'injecte ne se construit pas — le
        // message part en erreur à la consommation, pas au démarrage.
        //
        // Et `IIdempotencyStore` non enregistré, `IdempotencyEndpointFilter`
        // LAISSE PASSER : il journalise en Erreur puis exécute la requête SANS
        // protection contre le rejeu. C'est le pire des cas — la route a l'air
        // protégée, le filtre est posé, et un double appel crée deux produits.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IConsumerInbox, EfConsumerInbox<CatalogDbContext>>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<CatalogDbContext>>();

        // ═════════════════════════════════════════════════════════════════════
        // LE BARÈME DES OFFRES — SOURCE UNIQUE, VALIDÉE AU DÉMARRAGE.
        //
        // SON ABSENCE N'ÉTAIT PAS UNE ERREUR DISCRÈTE : `OfferCommandHandler`
        // exige `IOfferPricingSettings`, et la validation du conteneur au
        // démarrage a refusé de construire le service — dix exceptions, une par
        // commande d'offre. catalog-service ne démarrait plus du tout, et les
        // migrations ne s'appliquaient donc pas non plus. C'est ce qui expliquait
        // l'absence de la table `product_offers`.
        //
        // On peut lire cet échec comme une bonne nouvelle : la validation stricte
        // du conteneur a transformé une dépendance oubliée en refus de démarrage,
        // plutôt qu'en `NullReferenceException` à la première mise en vente.
        //
        // `PlatformPricing` PLUTÔT QU'UNE CLÉ À NOUS. Elle lit `Pricing:*`,
        // refuse une valeur aberrante, et rejette les anciennes clés au lieu de
        // retomber en silence sur un défaut. C'est la source que financial-service
        // utilise déjà pour rémunérer le vendeur : les deux calculs qui doivent
        // s'inverser l'un l'autre ne peuvent plus diverger.
        // ═════════════════════════════════════════════════════════════════════
        var bareme = new PlatformPricing(configuration);
        services.AddSingleton(bareme);
        services.AddSingleton<IOfferPricingSettings>(new OfferPricingSettings(bareme));
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICatalogModuleApi, CatalogModuleApi>();

        // INTERFACE DISTINCTE, PAS UNE MÉTHODE DE PLUS SUR `ICatalogModuleApi`.
        //
        // Les deux servent le même service, mais pas la même donnée ni le même
        // rythme : les fiches sont en cache-aside, les prix ne le sont pas — un
        // prix périmé de trente secondes est un prix faux. Les mêler obligerait à
        // choisir une politique de cache pour les deux.
        services.AddScoped<IOfferModuleApi, OfferModuleApi>();

        // ═════════════════════════════════════════════════════════════════════
        // AUCUN STOCKAGE ICI, ET C'EST LE POINT DE CETTE BASCULE.
        //
        // Ce module portait TROIS implémentations de stockage — Cloudflare R2,
        // HBA Media Core, et un simulateur — choisies au démarrage selon la
        // configuration présente. Sellers en avait deux autres, écrites
        // séparément, avec leur propre signature S3. Cinq façons de ranger un
        // fichier, cinq endroits où corriger une règle de nommage ou une durée
        // de rétention.
        //
        // Le service média (`HBA.Media`) est désormais le seul à connaître les
        // octets. Catalog ne manipule plus que des identifiants, et le dépôt se
        // fait à la frontière HTTP — voir `SellerCatalogEndpoints`.
        //
        // LE TRAITEMENT D'IMAGE, LUI, RESTE ICI. Détourer une photo sur fond
        // blanc n'est pas une règle de stockage mais une règle de PRÉSENTATION
        // du catalogue : elle ne concerne ni les pièces d'identité, ni les
        // justificatifs de livraison, ni les factures. La déplacer dans le
        // service média y aurait installé une dépendance à Cloudinary et à rembg
        // dont aucun autre appelant n'a l'usage.
        // ═════════════════════════════════════════════════════════════════════

        // ─────────────────────────────────────────────────────────────────────────
        // TRAITEMENT D'IMAGE : UN CHOIX AU DÉMARRAGE, PAS UN REPLI À CHAUD.
        //
        // Les deux implémentations rendent le même service — détourage puis fond
        // blanc — et ne servent QU'À CELA : l'image finale part sur R2 par le flux de
        // création. rembg passe devant Cloudinary quand il est configuré : pas de
        // quota, pas de facture à l'usage, et les photos des vendeurs ne quittent pas
        // l'infrastructure.
        //
        // CE N'EST PAS UNE CHAÎNE DE SECOURS. L'adaptateur est choisi ICI, une fois,
        // et ne change plus. Si le conteneur rembg tombe, les détourages échouent —
        // ils NE BASCULENT PAS sur Cloudinary. C'est assumé : un basculement
        // silencieux vers un service payant, déclenché par une panne, est exactement
        // le genre de mécanisme qu'on découvre sur une facture.
        //
        // Pour revenir à Cloudinary, on vide `Media:Rembg:BaseUrl` et on redémarre.
        //
        // Sans aucun des deux, `NullImageProcessor` renvoie l'image inchangée. Il ne
        // se déclare PAS disponible (`IImageProcessingAvailability`), ce qui permet
        // aux interfaces de ne pas promettre un détourage qui n'aura pas lieu.
        // ─────────────────────────────────────────────────────────────────────────
        var rembg = BindRembgOptions(configuration);
        var cloudinary = BindCloudinaryOptions(configuration);
        services.AddSingleton(rembg);
        services.AddSingleton(cloudinary);

        if (rembg.IsConfigured)
        {
            services.AddHttpClient(RembgImageProcessor.ClientName, client =>
            {
                // Le délai est porté par un CancellationToken dans l'adaptateur, afin
                // de distinguer « trop lent » d'« annulé par l'appelant ». On laisse
                // néanmoins une borne large ici : un HttpClient sans limite garderait
                // une socket ouverte indéfiniment si le service se fige.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(30, rembg.TimeoutSeconds) + 30);
            });
            // Santé partagée par tout le processus : une panne constatée par une
            // requête doit être connue de la suivante, et de l'endpoint de capacités.
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

        // ═════════════════════════════════════════════════════════════════════
        // LE MARQUEUR DE DISPONIBILITÉ SE RÉSOUT PAR RENVOI, ET IL MANQUAIT.
        //
        // Les trois implémentations portent DEUX interfaces — `IImageProcessor` et
        // `IImageProcessingAvailability` — mais seule la première était
        // enregistrée. Le marqueur n'apparaissait que dans un COMMENTAIRE. Tant
        // que personne ne l'injectait, le défaut est resté invisible ; la première
        // route qui l'a demandé (`/products/images/process`) a fait refuser le
        // démarrage du service, et l'application a affiché « Une erreur est
        // survenue » sur TOUS ses écrans catalogue — la liste des produits
        // comprise, qui n'a rien à voir avec le détourage.
        //
        // UN RENVOI, PAS UN SECOND `AddScoped`. Réenregistrer la classe
        // concrète créerait DEUX instances par requête : deux clients HTTP, et
        // surtout deux vues de la santé du service — celle qui répond à
        // `IsAvailable` ne serait pas celle qui a constaté la panne.
        //
        // Placé APRÈS le if/else à dessein : le renvoi est vrai quelle que soit la
        // branche choisie, et le mettre dans chacune des trois laisserait la
        // quatrième — celle qu'on ajoutera un jour — sans marqueur.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IImageProcessingAvailability>(sp =>
            (IImageProcessingAvailability)sp.GetRequiredService<IImageProcessor>());

        // Handlers de domain events (résolus par le DomainEventDispatcher).
        services.AddScoped<IDomainEventHandler<ProductCreatedDomainEvent>, ProductCreatedDomainEventHandler>();
        // HUIT ENREGISTREMENTS LÀ OÙ IL Y EN AVAIT UN — ET CHACUN COMPTE.
        //
        // Un fait du cycle de vie sans enregistrement est levé par l'agrégat, ne
        // trouve aucun handler, et disparaît. Rien n'échoue : le produit change
        // bien de statut, seul l'extérieur ne l'apprend jamais. C'est le défaut
        // que `scripts/check-event-consumers.py` existe pour rendre visible, et il
        // ne se voit pas autrement.
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
        //
        // L'événement partirait de l'agrégat et ne serait relayé par personne :
        // aucune erreur, aucun message dans l'outbox, et le fichier resterait
        // dans le stockage sans que rien ne le désigne.
        services.AddScoped<IDomainEventHandler<ProductMediaRemovedDomainEvent>, ProductMediaRemovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<BrandCreatedDomainEvent>, BrandCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CategoryCreatedDomainEvent>, CategoryCreatedDomainEventHandler>();

        // Handlers d'events d'intégration venus du module Sellers : Catalog réagit au
        // cycle de vie du compte vendeur (fermeture -> dépublication, suppression ->
        // archivage des produits).
        services.AddScoped<IIntegrationEventHandler<SellerClosedIntegrationEvent>, SellerClosedProductInvalidationHandler>();
        services.AddScoped<IIntegrationEventHandler<SellerDeletedIntegrationEvent>, SellerDeletedProductPurgeHandler>();

        // SANS CES DEUX LIGNES, SUSPENDRE UN VENDEUR NE RETIRE RIEN (ISSUE-025).
        //
        // Le répartiteur d'événements d'intégration résout PARESSEUSEMENT : un
        // événement sans gestionnaire enregistré ne provoque aucune erreur, aucun
        // avertissement. Il est marqué traité et disparaît. C'est exactement ce qui
        // arrivait à `SellerSuspendedIntegrationEvent` — publié depuis le premier
        // jour, y compris sur refus de dossier KYB, et consommé par personne d'autre
        // qu'une notification.
        services.AddScoped<IIntegrationEventHandler<SellerSuspendedIntegrationEvent>, SellerSuspendedOfferWithdrawalHandler>();
        services.AddScoped<IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>, SellerSuspensionLiftedOfferReinstatementHandler>();

        // ET SANS CES TROIS-CI, FERMER UNE BOUTIQUE NE RETIRE RIEN (ISSUE-041).
        //
        // Même mécanique, un cran plus bas en granularité : seller-service publiait
        // les quatre événements du cycle de vie d'une boutique, catalog écoutait le
        // topic, et `SuspendStoreCatalogCommand` n'avait aucun appelant.
        //
        // Il n'y en a pas pour `StoreSuspensionLiftedIntegrationEvent` : lever la
        // sanction repasse la boutique en `Closed`, pas en `Open`. Les offres
        // doivent rester retirées jusqu'à ce que le vendeur rouvre.
        services.AddScoped<IIntegrationEventHandler<StoreClosedIntegrationEvent>, StoreClosedOfferWithdrawalHandler>();
        services.AddScoped<IIntegrationEventHandler<StoreSuspendedIntegrationEvent>, StoreSuspendedOfferWithdrawalHandler>();
        services.AddScoped<IIntegrationEventHandler<StoreOpenedIntegrationEvent>, StoreOpenedOfferReinstatementHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // ET SANS CES DEUX-CI, LE STOCK NE DÉCIDE DE RIEN (ISSUE-047).
        //
        // Aucune offre n'est jamais passée `OutOfStock`, ni n'est jamais revenue
        // en vente. `MarkOfferOutOfStockCommand` existait sans émetteur ;
        // `ListBySkuAsync` avait été écrite POUR ce cas — « Inventory s'en sert
        // pour signaler une rupture », dit son commentaire ; le contrat
        // d'inventaire annonçait « consommé par Offers ». Cinq fichiers
        // décrivaient un chemin que rien ne parcourait.
        //
        // Conséquence dans les deux sens : une offre en rupture restait
        // ACHETABLE — l'acheteur découvrait l'indisponibilité au checkout, après
        // avoir choisi son adresse — et un réassort ne remettait rien en vente.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IIntegrationEventHandler<StockDepletedIntegrationEvent>, WithdrawOffersOnStockDepletedHandler>();
        services.AddScoped<IIntegrationEventHandler<StockReplenishedIntegrationEvent>, ReactivateOffersOnStockReplenishedHandler>();

        // Validators FluentValidation du module.
        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // Processeur d'outbox dédié au DbContext du module.
        services.AddOutboxProcessor<CatalogDbContext>();
    }

    /// <summary>
    /// Lie « Media:Rembg ». Absence de section = fonction inactive, jamais d'erreur au
    /// démarrage : une installation sans détourage doit rester une installation qui
    /// démarre.
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
