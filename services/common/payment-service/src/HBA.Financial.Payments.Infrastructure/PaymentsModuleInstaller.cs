using HBA.Shared.Infrastructure.Hosting;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Shared.Infrastructure.Idempotency;
using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Application.Abstractions.Gateways;
using HBA.Financial.Payments.Application.Payments.Commands.InitiatePayment;
using HBA.Financial.Payments.Application.Payments.EventHandlers;
using HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Financial.Payments.Contracts;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Financial.Payments.Domain.PaymentMethods;
using HBA.Financial.Payments.Domain.Payments.Events;
using HBA.Financial.Payments.Infrastructure.Gateways;
using HBA.Financial.Payments.Infrastructure.Gateways.Real;
using HBA.Financial.Payments.Infrastructure.Gateways.Simulation;
using HBA.Financial.Payments.Infrastructure.Persistence;
using HBA.Financial.Payments.Infrastructure.Public;

using HBA.Financial.Payments.Infrastructure.Caching.Redis;
using HBA.Financial.Payments.Infrastructure.Observability;
using HBA.Financial.Payments.Infrastructure.Idempotency;
namespace HBA.Financial.Payments.Infrastructure;

/// <summary>
/// Enregistre le module Payments : DbContext, repository, API publique, handlers,
/// validators, outbox.
/// </summary>
public sealed class PaymentsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Payments";

    public Assembly ApplicationAssembly => typeof(InitiatePaymentCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheFinancialPayments(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteFinancialPayments(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFinancialPayments()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentsDbContext.SchemaName)));

        services.AddScoped<IPaymentsUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // LA LECTURE DE LA COMMANDE À PAYER PASSE PAR UN SEUL POINT (lot 6.1).
        services.AddScoped<IPayableOrderReader, PayableOrderReader>();

        // Socle du §5 et du §19.5 — mêmes enregistrements que user-service et
        // identity-service, pour que les trois se comportent pareil.
        services.AjouterIdempotenceFinancialPayments();
        services.AddScoped<ISavedPaymentMethodRepository, SavedPaymentMethodRepository>();
        services.AddScoped<IPaymentsModuleApi, PaymentsModuleApi>();

        // Prestataires de paiement : adaptateurs + résolveur par nom.
        var stripeOptions = BindStripeOptions(configuration);
        var paypalOptions = BindPayPalOptions(configuration);
        var mtnOptions = BindMtnMomoOptions(configuration);
        var moovOptions = BindMoovOptions(configuration);
        var fedapayOptions = BindFedaPayOptions(configuration);
        services.AddSingleton(stripeOptions);
        services.AddSingleton(paypalOptions);
        services.AddSingleton(mtnOptions);
        services.AddSingleton(moovOptions);
        services.AddSingleton(fedapayOptions);

        // LE GARDE-FOU LE PLUS IMPORTANT DE TOUT LE PROJET.
        var isProduction = IsProduction(configuration);

        ConfigureUnsignedWebhookPolicy(configuration, isProduction);

        var realGateways = new List<string>();
        var simulatedGateways = new List<string>();

        // Passerelles RÉELLES enregistrées dont `RefundAsync` ne fait aucun appel :
        // elles répondent « échec » en dur.
        var gatewaysSansRemboursement = new List<string>();

        RegisterGateway(
            services, isProduction, stripeOptions.IsConfigured, "Stripe", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(StripeHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(stripeOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, StripeHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, StripePaymentGateway>());

        RegisterGateway(
            services, isProduction, paypalOptions.IsConfigured, "PayPal", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(PayPalHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(paypalOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, PayPalHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>(),
            supportsRefund: PayPalHttpGateway.RefundSupported,
            gatewaysSansRemboursement: gatewaysSansRemboursement);

        RegisterGateway(
            services, isProduction, mtnOptions.IsConfigured, "MtnMomo", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(MtnMomoHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(mtnOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, MtnMomoHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, MtnMomoPaymentGateway>(),
            supportsRefund: MtnMomoHttpGateway.RefundSupported,
            gatewaysSansRemboursement: gatewaysSansRemboursement);

        RegisterGateway(
            services, isProduction, moovOptions.IsConfigured, "Moov", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(MoovHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(moovOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, MoovHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, MoovPaymentGateway>(),
            supportsRefund: MoovHttpGateway.RefundSupported,
            gatewaysSansRemboursement: gatewaysSansRemboursement);

        RegisterGateway(
            services, isProduction, fedapayOptions.IsConfigured, "FedaPay", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(FedaPayHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(fedapayOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, FedaPayHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, FedaPayPaymentGateway>(),
            supportsRefund: FedaPayHttpGateway.RefundSupported,
            gatewaysSansRemboursement: gatewaysSansRemboursement);

        // En production, une plateforme SANS aucun moyen d'encaisser n'est pas une
        // plateforme : c'est un catalogue.
        if (isProduction && realGateways.Count == 0)
        {
            throw new InvalidOperationException(
                "PRODUCTION SANS AUCUN PRESTATAIRE DE PAIEMENT CONFIGURÉ.\n\n" +
                "Aucune clé PSP n'a été trouvée (Payments:FedaPay:ApiKey, Payments:Stripe:ApiKey, …).\n" +
                "Le démarrage est refusé DÉLIBÉRÉMENT : sans ce garde-fou, la plateforme retomberait sur des " +
                "passerelles SIMULÉES qui marquent toute commande comme « payée » sans qu'aucun argent ne bouge.\n\n" +
                "Vérifiez l'injection des secrets (vault Ansible / variables d'environnement du conteneur).");
        }

        // Bruyant, et volontairement.
        if (simulatedGateways.Count > 0)
        {
            Console.WriteLine(
                $"[Payments]  PASSERELLES SIMULÉES ACTIVES : {string.Join(", ", simulatedGateways)}. " +
                "Ces prestataires acceptent TOUT paiement sans qu'aucun argent ne bouge. " +
                (realGateways.Count > 0
                    ? $"Passerelles réelles : {string.Join(", ", realGateways)}."
                    : "AUCUNE passerelle réelle configurée."));
        }

        // UNE PASSERELLE QUI NE SAIT PAS REMBOURSER LE DIT AU DÉMARRAGE, PAS LE
        // JOUR OÙ UN CLIENT RÉCLAME SON ARGENT.
        if (gatewaysSansRemboursement.Count > 0)
        {
            Console.WriteLine(
                $"[Payments] ⓘ  REMBOURSEMENT PSP INDISPONIBLE chez : {string.Join(", ", gatewaysSansRemboursement)}. "
                + "Ces adaptateurs ne font aucun appel de remboursement (FedaPay n'expose pas d'API). "
                + "Décision D33 : un remboursement sur ces prestataires CRÉDITE LE PORTEFEUILLE DU CLIENT ; "
                + "le virement Mobile Money est une demande distincte, validée à la main par un administrateur.");
        }

        // Une clé et une URL qui ne désignent pas le même monde, c'est le pire des
        // cas : soit tout échoue en 403 sans qu'on comprenne pourquoi, soit — si la
        // clé est live — de l'argent RÉEL part alors qu'on croyait tester.
        if (fedapayOptions.IsConfigured && !fedapayOptions.KeyMatchesEnvironment)
        {
            throw new InvalidOperationException(
                "Configuration FedaPay incohérente : la clé API et Payments:FedaPay:BaseUrl ne désignent pas le même environnement " +
                $"(BaseUrl = « {fedapayOptions.BaseUrl} »). Une clé sk_live_… exige https://api.fedapay.com/v1 ; " +
                "une clé sk_sandbox_… exige https://sandbox-api.fedapay.com/v1.");
        }

        // Versements (payouts vendeur) : réels via FedaPay UNIQUEMENT en LIVE, clé
        // configurée et flag activé — voir FedaPayOptions.CanPayout.
        if (fedapayOptions.CanPayout)
        {
            services.AddSingleton<IPayoutGateway, FedaPayPayoutGateway>();
        }
        else
        {
            if (fedapayOptions.EnablePayouts && fedapayOptions.IsSandbox)
            {
                // Cette combinaison est une erreur de configuration, pas un choix :
                // elle mérite une trace explicite au démarrage.
                Console.Error.WriteLine(
                    "[FedaPay] EnablePayouts=true avec l'API bac à sable : les versements réels sont IMPOSSIBLES " +
                    "en sandbox (403). Les retraits seront SIMULÉS. Pour des versements réels, passez " +
                    "Payments:FedaPay:BaseUrl sur https://api.fedapay.com/v1 avec une clé sk_live_…, et faites " +
                    "activer les dépôts sur le compte marchand par FedaPay.");
            }

            // EN PRODUCTION, ON REFUSE DE DÉMARRER PLUTÔT QUE DE SIMULER.
            if (isProduction)
            {
                throw new InvalidOperationException(
                    "Aucune passerelle de versement réelle n'est disponible en production. "
                    + "FedaPay ne peut verser que sur l'API LIVE, avec une clé sk_live_… et "
                    + "Payments:FedaPay:EnablePayouts=true (voir FedaPayOptions.CanPayout). "
                    + "Sans elle, les retraits vendeur seraient clôturés « payés » sans qu'aucun "
                    + "argent ne parte : le service refuse de démarrer.");
            }

            services.AddSingleton<IPayoutGateway, SimulatedPayoutGateway>();
        }

        services.AddScoped<IPayoutModuleApi, PayoutModuleApi>();

        services.AddSingleton<IPaymentGatewayResolver, PaymentGatewayResolver>();

        // ENREGISTREMENT EXPLICITE, DONC OUBLIABLE.
        services.AddScoped<IDomainEventHandler<PaymentInitiatedDomainEvent>, PaymentInitiatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentCapturedDomainEvent>, PaymentCapturedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentFailedDomainEvent>, PaymentFailedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentRefundedDomainEvent>, PaymentRefundedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentRefundFailedDomainEvent>, PaymentRefundFailedDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }

    /// <summary>
    /// Décide si un webhook NON SIGNÉ peut être accepté lorsque le secret du
    /// prestataire est absent.
    /// </summary>
    private static void ConfigureUnsignedWebhookPolicy(IConfiguration configuration, bool isProduction)
    {
        var demande = bool.TryParse(
            configuration["Payments:AllowUnsignedWebhooksWhenSecretMissing"], out var valeur) && valeur;

        GatewayWebhook.AllowUnsignedWhenSecretMissing = demande && !isProduction;

        if (demande && isProduction)
        {
            Console.Error.WriteLine(
                "[Payments] Payments:AllowUnsignedWebhooksWhenSecretMissing=true est IGNORÉ en production. "
                + "Un prestataire sans secret de webhook verra ses notifications rejetées : injectez "
                + "Payments__<Prestataire>__WebhookSecret.");
            return;
        }

        // Bruyant, et volontairement : un développeur qui teste un encaissement
        // doit savoir que la seule serrure de la route est levée.
        if (GatewayWebhook.AllowUnsignedWhenSecretMissing)
        {
            Console.WriteLine(
                "[Payments]  WEBHOOKS NON SIGNÉS ACCEPTÉS pour les prestataires sans WebhookSecret. "
                + "La route /api/financial/payments/webhooks/{provider} est anonyme : dans cet état, "
                + "n'importe qui peut déclarer un paiement encaissé. Développement UNIQUEMENT.");
        }
    }

    private static StripeOptions BindStripeOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:Stripe");
        var options = new StripeOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            SuccessUrl = section["SuccessUrl"] ?? string.Empty,
            CancelUrl = section["CancelUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["CheckoutBaseUrl"]))
        {
            options.CheckoutBaseUrl = section["CheckoutBaseUrl"]!;
        }
        return options;
    }

    private static PayPalOptions BindPayPalOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:PayPal");
        var options = new PayPalOptions
        {
            ClientId = section["ClientId"] ?? string.Empty,
            Secret = section["Secret"] ?? string.Empty,
            WebhookId = section["WebhookId"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            ReturnUrl = section["ReturnUrl"] ?? string.Empty,
            CancelUrl = section["CancelUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["CheckoutBaseUrl"]))
        {
            options.CheckoutBaseUrl = section["CheckoutBaseUrl"]!;
        }
        return options;
    }

    private static MtnMomoOptions BindMtnMomoOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:MtnMomo");
        var options = new MtnMomoOptions
        {
            SubscriptionKey = section["SubscriptionKey"] ?? string.Empty,
            ApiUser = section["ApiUser"] ?? string.Empty,
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["TargetEnvironment"]))
        {
            options.TargetEnvironment = section["TargetEnvironment"]!;
        }
        return options;
    }

    private static FedaPayOptions BindFedaPayOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:FedaPay");
        var options = new FedaPayOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty,
            // Active les versements réels FedaPay (sinon payout simulé).
            EnablePayouts = bool.TryParse(section["EnablePayouts"], out var enablePayouts) && enablePayouts
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        // Méthode de transfert payout (« mode » FedaPay).
        if (!string.IsNullOrWhiteSpace(section["PayoutMode"]))
        {
            options.PayoutMode = section["PayoutMode"]!;
        }
        return options;
    }

    // BaseAddress doit finir par « / » pour que les URI relatifs des requêtes se
    // concatènent correctement.
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    /// <summary>
    /// Enregistre un PSP : l'adaptateur RÉEL s'il est configuré ; sinon la
    /// simulation — et UNIQUEMENT hors production.
    /// </summary>
    private static void RegisterGateway(
        IServiceCollection services,
        bool isProduction,
        bool isConfigured,
        string providerName,
        List<string> realGateways,
        List<string> simulatedGateways,
        Action registerReal,
        Action registerSimulated,
        bool supportsRefund = true,
        List<string>? gatewaysSansRemboursement = null)
    {
        if (isConfigured)
        {
            registerReal();
            realGateways.Add(providerName);

            // On ne le découvre pas au moment de rembourser : on le sait ICI.
            if (!supportsRefund)
            {
                gatewaysSansRemboursement?.Add(providerName);
            }

            return;
        }

        if (isProduction)
        {
            // Non configuré ET en production : on n'enregistre RIEN. Ce prestataire
            // n'existe tout simplement pas pour cette instance.
            return;
        }

        registerSimulated();
        simulatedGateways.Add(providerName);
    }

    /// <summary>Sommes-nous en production ?</summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        return EnvironnementDeploiement.EstProduction(configuration);
    }

    private static MoovOptions BindMoovOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:Moov");
        var options = new MoovOptions
        {
            MerchantId = section["MerchantId"] ?? string.Empty,
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["TokenPath"]))
        {
            options.TokenPath = section["TokenPath"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["PaymentPath"]))
        {
            options.PaymentPath = section["PaymentPath"]!;
        }
        return options;
    }
}
