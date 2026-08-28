using HBA.Shared.Infrastructure.Hosting;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Idempotency;
using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Application.Abstractions.Gateways;
using HBA.Financial.Payments.Application.Payments.Commands.InitiatePayment;
using HBA.Financial.Payments.Application.Payments.EventHandlers;
using HBA.Financial.Payments.Contracts;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Financial.Payments.Domain.PaymentMethods;
using HBA.Financial.Payments.Domain.Payments.Events;
using HBA.Financial.Payments.Infrastructure.Gateways;
using HBA.Financial.Payments.Infrastructure.Gateways.Real;
using HBA.Financial.Payments.Infrastructure.Gateways.Simulation;
using HBA.Financial.Payments.Infrastructure.Persistence;
using HBA.Financial.Payments.Infrastructure.Public;

namespace HBA.Financial.Payments.Infrastructure;

/// <summary>Enregistre le module Payments : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class PaymentsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Payments";

    public Assembly ApplicationAssembly => typeof(InitiatePaymentCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentsDbContext.SchemaName)));

        services.AddScoped<IPaymentsUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // LA LECTURE DE LA COMMANDE À PAYER PASSE PAR UN SEUL POINT (lot 6.1).
        //
        // Il délègue à `IOrderingModuleApi` ou à `IMealOrderModuleApi` selon
        // l'univers annoncé. Les deux sont des clients gRPC enregistrés par l'hôte
        // (`AddOrderingGrpcClient`, `AddFoodOrdersGrpcClient`) : si l'un des deux
        // manque, c'est le démarrage qui échoue, pas le premier paiement.
        services.AddScoped<IPayableOrderReader, PayableOrderReader>();

        // Socle du §5 et du §19.5 — mêmes enregistrements que user-service et
        // identity-service, pour que les trois se comportent pareil.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<PaymentsDbContext>>();
        // LE MAGASIN ET SON PURGEUR, EN UN SEUL GESTE.
        //
        // `ExpiresAtUtc` existait depuis le début, avec son index de purge, et
        // aucune ligne de code ne la lisait : une réservation inachevée bloquait
        // sa clé pour toujours (audit 1.8). Les deux enregistrements sont
        // désormais indissociables — voir `IdempotencyRegistration` pour la
        // raison, qui tient en une phrase : un huitième service qui ne copierait
        // que la première ligne n'aurait jamais de purge, sans rien signaler.
        services.AddIdempotence<PaymentsDbContext>();
        services.AddScoped<ISavedPaymentMethodRepository, SavedPaymentMethodRepository>();
        services.AddScoped<IPaymentsModuleApi, PaymentsModuleApi>();

        // Prestataires de paiement : adaptateurs + résolveur par nom.
        // Options liées manuellement depuis la config (aucune dépendance IOptions).
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

        // ─────────────────────────────────────────────────────────────────────────
        // LE GARDE-FOU LE PLUS IMPORTANT DE TOUT LE PROJET.
        //
        // AVANT : chaque PSP non configuré retombait SILENCIEUSEMENT sur une passerelle
        // SIMULÉE. Or les clés sont vides par défaut dans appsettings.json. Un
        // déploiement qui oubliait d'injecter les secrets démarrait donc normalement…
        // et simulait TOUS les paiements :
        //
        //   • SimulatedPaymentGateway.GetStatusAsync() renvoie TOUJOURS « Captured » ;
        //   • RefundAsync() renvoie TOUJOURS « Success » ;
        //   • la vérification de signature des webhooks est court-circuitée.
        //
        // Autrement dit : la plateforme encaissait des commandes marquées PAYÉES sans
        // qu'un seul franc ne bouge, et acceptait n'importe quel faux webhook. Aucune
        // erreur, aucun log, aucun symptôme — jusqu'à ce qu'un vendeur réclame l'argent
        // d'une vente qui n'a jamais été payée.
        //
        // DÉSORMAIS : en PRODUCTION, une passerelle simulée n'est JAMAIS enregistrée.
        // Un PSP non configuré est simplement ABSENT du conteneur, et le résolveur
        // renvoie une erreur franche (« Prestataire de paiement non pris en charge »).
        // Un paiement impossible vaut infiniment mieux qu'un paiement imaginaire.
        //
        // Et si AUCUNE passerelle réelle n'est configurée en production, on refuse de
        // démarrer (voir plus bas). Une plateforme qui ne boote pas se répare en cinq
        // minutes ; une plateforme qui simule ses encaissements, jamais tout à fait.
        // ─────────────────────────────────────────────────────────────────────────
        var isProduction = IsProduction(configuration);

        ConfigureUnsignedWebhookPolicy(configuration, isProduction);

        var realGateways = new List<string>();
        var simulatedGateways = new List<string>();

        // Passerelles RÉELLES enregistrées dont `RefundAsync` ne fait aucun appel :
        // elles répondent « échec » en dur. Voir le garde-fou plus bas.
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
        // plateforme : c'est un catalogue. On refuse de démarrer plutôt que d'accepter
        // des commandes qu'on ne saura jamais faire payer.
        if (isProduction && realGateways.Count == 0)
        {
            throw new InvalidOperationException(
                "PRODUCTION SANS AUCUN PRESTATAIRE DE PAIEMENT CONFIGURÉ.\n\n" +
                "Aucune clé PSP n'a été trouvée (Payments:FedaPay:ApiKey, Payments:Stripe:ApiKey, …).\n" +
                "Le démarrage est refusé DÉLIBÉRÉMENT : sans ce garde-fou, la plateforme retomberait sur des " +
                "passerelles SIMULÉES qui marquent toute commande comme « payée » sans qu'aucun argent ne bouge.\n\n" +
                "Vérifiez l'injection des secrets (vault Ansible / variables d'environnement du conteneur).");
        }

        // Bruyant, et volontairement. En production ce cas est impossible (voir
        // ci-dessus) ; ailleurs, il faut qu'un développeur qui teste un paiement sache
        // qu'il teste une FICTION — sans quoi il conclura que « ça marche ».
        if (simulatedGateways.Count > 0)
        {
            Console.WriteLine(
                $"[Payments]  PASSERELLES SIMULÉES ACTIVES : {string.Join(", ", simulatedGateways)}. " +
                "Ces prestataires acceptent TOUT paiement sans qu'aucun argent ne bouge. " +
                (realGateways.Count > 0
                    ? $"Passerelles réelles : {string.Join(", ", realGateways)}."
                    : "AUCUNE passerelle réelle configurée."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // UNE PASSERELLE QUI NE SAIT PAS REMBOURSER LE DIT AU DÉMARRAGE, PAS LE
        // JOUR OÙ UN CLIENT RÉCLAME SON ARGENT.
        //
        // `FedaPayHttpGateway.RefundAsync`, et ses équivalents MTN, Moov et PayPal,
        // renvoient `Success:false` EN DUR : aucun appel n'est fait, aucun argent ne
        // repart par le chemin qui l'a apporté. Ce n'est pas une lacune du code —
        // FedaPay n'expose pas d'API de remboursement.
        //
        // CE GARDE-FOU REFUSAIT LE DÉMARRAGE EN PRODUCTION. IL NE LE FAIT PLUS,
        // ET LA RAISON N'EST PAS UN ASSOUPLISSEMENT.
        //
        // Sa prémisse était : « aucun client payé par eux ne sera JAMAIS remboursé
        // automatiquement ». Elle était vraie, et elle a cessé de l'être. Depuis la
        // décision D33, `RefundPaymentCommandHandler` rend l'argent sur le
        // PORTEFEUILLE du client quand le prestataire ne sait pas le faire : le
        // client est remboursé quoi qu'il arrive, immédiatement, et demande un
        // virement Mobile Money quand il le veut.
        //
        // Le drapeau `Payments:AllowGatewaysWithoutRefund` a disparu avec la
        // prémisse. Il n'actait plus rien : il n'y a plus de dette à assumer.
        //
        // CE QUI RESTE, ET POURQUOI ON LE DIT QUAND MÊME.
        //
        // Le chemin de retour change. Un exploitant doit savoir qu'avec ces
        // prestataires l'argent ne repart PAS sur le moyen de paiement du client
        // mais sur son solde interne — c'est ce qui explique les demandes de
        // virement qui arriveront dans la file d'administration, et c'est ce qui
        // fait que la plateforme porte désormais une dette envers ses clients.
        // ═════════════════════════════════════════════════════════════════════
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
        // clé est live — de l'argent RÉEL part alors qu'on croyait tester. On refuse
        // de démarrer. Une plateforme de paiement qui ne boote pas se répare en cinq
        // minutes ; une plateforme qui paie au mauvais endroit, jamais tout à fait.
        if (fedapayOptions.IsConfigured && !fedapayOptions.KeyMatchesEnvironment)
        {
            throw new InvalidOperationException(
                "Configuration FedaPay incohérente : la clé API et Payments:FedaPay:BaseUrl ne désignent pas le même environnement " +
                $"(BaseUrl = « {fedapayOptions.BaseUrl} »). Une clé sk_live_… exige https://api.fedapay.com/v1 ; " +
                "une clé sk_sandbox_… exige https://sandbox-api.fedapay.com/v1.");
        }

        // Versements (payouts vendeur) : réels via FedaPay UNIQUEMENT en LIVE, clé
        // configurée et flag activé — voir FedaPayOptions.CanPayout.
        //
        // Le bac à sable FedaPay N'EXÉCUTE PAS les dépôts Mobile Money : il refuse la
        // création avec « 403 Opération non autorisée ». Y activer les payouts ne
        // produisait qu'une file de retraits en échec, remboursés, et un vendeur
        // persuadé que la plateforme lui devait de l'argent. On simule donc, et on le
        // DIT — plutôt que d'échouer en silence.
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

            // ═════════════════════════════════════════════════════════════════
            // EN PRODUCTION, ON REFUSE DE DÉMARRER PLUTÔT QUE DE SIMULER.
            //
            // `SimulatedPayoutGateway` clôt le retrait en « payé ». Le solde du
            // vendeur est débité, la ligne de versement est marquée réussie, et
            // AUCUN ARGENT NE PART. Le vendeur constate un solde à zéro et
            // n'a rien reçu ; le système, lui, croit l'avoir payé. Il n'existe
            // aucun moyen de distinguer après coup un versement simulé d'un
            // versement réel qui se serait perdu — il faut rapprocher les
            // relevés du prestataire à la main, dossier par dossier.
            //
            // C'est la même règle que pour les encaissements (`realGateways.Count
            // == 0` plus haut) et que pour l'e-mail côté Notifications : un
            // adaptateur silencieux fait « tourner » la plateforme dans un état
            // faux. Un démarrage refusé se remarque tout de suite, et se corrige
            // en une variable de configuration.
            //
            // Le repli simulé reste disponible partout ailleurs qu'en production :
            // un développeur doit pouvoir dérouler un retrait de bout en bout.
            // ═════════════════════════════════════════════════════════════════
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
        //
        // Le répartiteur résout `IDomainEventHandler<T>` par le conteneur : un
        // handler non enregistré n'est pas appelé, et RIEN ne le signale — ni
        // compilation, ni exception, ni journal. L'événement part dans le vide.
        // C'est ainsi que `payment.created` a manqué jusqu'ici.
        services.AddScoped<IDomainEventHandler<PaymentInitiatedDomainEvent>, PaymentInitiatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentCapturedDomainEvent>, PaymentCapturedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentFailedDomainEvent>, PaymentFailedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentRefundedDomainEvent>, PaymentRefundedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentRefundFailedDomainEvent>, PaymentRefundFailedDomainEventHandler>();

        // Chorégraphie : libération de l'escrow à la livraison de la commande.
        services.AddScoped<IIntegrationEventHandler<OrderDeliveredIntegrationEvent>, ReleaseEscrowOnOrderDeliveredHandler>();

        // LE MÊME GESTE POUR LE FOOD, QUI N'EXISTAIT PAS.
        //
        // `MealOrderDeliveredIntegrationEvent` était publié sans aucun consommateur.
        // Invisible tant qu'aucun repas ne pouvait être payé ; impasse dès que le
        // lot 6.1 ouvre ce chemin — client débité, restaurateur jamais reversable.
        services.AddScoped<IIntegrationEventHandler<MealOrderDeliveredIntegrationEvent>, ReleaseEscrowOnMealOrderDeliveredHandler>();

        // CE MAILLON MANQUAIT : PERSONNE NE REMBOURSAIT.
        //
        // `OrderCancelled` avait deux consommateurs — la reprise des gains
        // vendeur et la notification au client. Aucun ne rendait l'argent. Le
        // monolithe le faisait dans un helper de sa composition root, qui avait
        // accès aux deux modules à la fois ; le geste s'est perdu à la découpe.
        //
        // C'est financial qui possède le paiement : c'est donc à lui de décider
        // ce qu'annuler implique.
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, RefundPaymentOnOrderCancelledHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<PaymentsDbContext>();
    }

    /// <summary>
    /// Décide si un webhook NON SIGNÉ peut être accepté lorsque le secret du
    /// prestataire est absent. Défaut : non.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'INTERRUPTEUR EXISTAIT ET N'ÉTAIT BRANCHÉ NULLE PART.
    ///
    /// `GatewayWebhook.AllowUnsignedWhenSecretMissing` était déclaré, documenté,
    /// et jamais posé — pendant que les adaptateurs concrets décidaient seuls et
    /// répondaient `true` sur secret vide (« sandbox permissif »).
    ///
    /// Ce qui cassait concrètement : `POST /api/financial/payments/webhooks/{p}`
    /// est `AllowAnonymous` — un PSP ne présente pas de JWT — et les secrets sont
    /// vides par défaut dans appsettings.json. N'importe qui pouvait donc poster
    /// un « payment_intent.succeeded » et faire passer une commande en payée :
    /// stock décrémenté, gains vendeur provisionnés, escrow en route. Sans une
    /// ligne de log.
    ///
    /// EN PRODUCTION, LE DRAPEAU EST IGNORÉ, MÊME POSÉ À `true`.
    ///
    /// C'est exactement la variable qu'on recopie d'un fichier d'environnement de
    /// recette vers celui de production. Un secret manquant en production est une
    /// erreur d'injection de secrets : elle se répare, elle ne se contourne pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
            // Active les versements réels FedaPay (sinon payout simulé). Clé lue
            // depuis Payments__FedaPay__EnablePayouts (« true »/« false »).
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
        // Méthode de transfert payout (« mode » FedaPay). Lue depuis
        // Payments__FedaPay__PayoutMode. Défaut mtn_open (MTN Mobile Money Bénin).
        if (!string.IsNullOrWhiteSpace(section["PayoutMode"]))
        {
            options.PayoutMode = section["PayoutMode"]!;
        }
        return options;
    }

    // BaseAddress doit finir par « / » pour que les URI relatifs des requêtes se concatènent correctement.
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    /// <summary>
    /// Enregistre un PSP : l'adaptateur RÉEL s'il est configuré ; sinon la simulation —
    /// et UNIQUEMENT hors production.
    ///
    /// En production, un PSP non configuré n'est pas remplacé : il est ABSENT. Le
    /// résolveur renverra alors « Prestataire de paiement non pris en charge », ce qui
    /// est une erreur honnête. Le remplacer par une simulation produirait, à l'inverse,
    /// un encaissement fictif — un mensonge silencieux, et le pire bug possible pour
    /// une plateforme qui manipule de l'argent.
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

    /// <summary>
    /// Sommes-nous en production ?
    /// </summary>
    /// <remarks>
    /// L'installeur ne reçoit qu'un <see cref="IConfiguration"/> — les modules
    /// s'installent avant que l'hôte ne soit construit, donc pas
    /// d'<c>IHostEnvironment</c>. La règle elle-même vit dans
    /// <c>EnvironnementDeploiement</c>, en un seul exemplaire.
    ///
    /// CE PARAGRAPHE DÉCRIVAIT AUPARAVANT UN FAIL-OPEN ASSUMÉ : « l'inconnu est
    /// traité comme pas la production, sinon un nom mal orthographié empêcherait
    /// de travailler ». Ce n'est plus vrai, et ce n'était pas défendable : une
    /// variable ABSENTE tombait du même côté qu'une faute de frappe, alors
    /// qu'ASP.NET Core considère une variable absente comme la production.
    /// Désormais l'inconnu et l'absent sont la production ; seuls les noms
    /// explicitement listés en dispensent.
    /// </remarks>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        //
        // Ce corps était une copie parmi six d'une règle FAIL-OPEN : tout ce qui
        // n'était pas littéralement « Production » — variable absente, chaîne
        // vide, faute de frappe — était traité comme du développement, alors
        // qu'ASP.NET Core, lui, considère une variable absente comme la
        // production. Voir l'encadré de `EnvironnementDeploiement`.
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
