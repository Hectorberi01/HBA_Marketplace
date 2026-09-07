using HBA.Shared.Infrastructure.Hosting;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Communication.Notifications.Domain.Templates;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using FirebaseAdmin.Messaging;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;
using HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Communication.Notifications.Application.Emails;
using HBA.Communication.Notifications.Application.Notifications.Queries;
using HBA.Communication.Notifications.Domain.Devices;
using HBA.Communication.Notifications.Domain.Notifications;
using HBA.Communication.Notifications.Domain.Preferences;
using HBA.Communication.Notifications.Infrastructure.Persistence;
using HBA.Communication.Notifications.Infrastructure.Email;
using HBA.Communication.Notifications.Infrastructure.Push;
using HBA.Communication.Notifications.Infrastructure.Sms;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Contracts.IntegrationEvents;
using HBA.Communication.Contracts.IntegrationEvents;
using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Deliveries.Contracts.IntegrationEvents;
// Lève l'ambiguïté avec FirebaseAdmin.Messaging.FcmOptions.
using FcmOptions = HBA.Communication.Notifications.Infrastructure.Push.FcmOptions;

using HBA.Communication.Notifications.Infrastructure.Caching.Redis;
using HBA.Communication.Notifications.Infrastructure.Observability;
using HBA.Communication.Notifications.Infrastructure.Idempotency;
namespace HBA.Communication.Notifications.Infrastructure;

/// <summary>
/// Enregistre le module Notifications : DbContext, repository, dispatcher et
/// consumers fan-out.
/// </summary>
public sealed class NotificationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Notifications";

    public Assembly ApplicationAssembly => typeof(ListMyNotificationsQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheCommunicationNotifications(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteCommunicationNotifications(configuration);

        // « Default », ET NON « Marketplace ».
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieCommunicationNotifications()`, que le composition root
        // peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName)));

        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();

        // Socle du §5 et du §19.5. LE MAGASIN ET SON PURGEUR, EN UN SEUL GESTE.
        services.AjouterIdempotenceCommunicationNotifications();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<NotificationDispatcher>();

        // Push (FCM) : si un compte de service Firebase est configuré, on branche
        // l'envoi réel ; sinon un adaptateur no-op (aucun push, mais tout
        // compile/tourne).
        var fcm = BindFcmOptions(configuration);
        services.AddSingleton(fcm);
        var fcmReady = false;
        if (fcm.IsConfigured)
        {
            // Le push est OPTIONNEL : un compte de service FCM MALFORMÉ (clé privée
            // invalide, JSON tronqué, `\n` mal échappés…) ne doit PAS abattre toute
            // la plateforme.
            try
            {
                if (FirebaseApp.DefaultInstance is null)
                {
                    FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(fcm.ResolveJson()) });
                }
                services.AddSingleton(FirebaseMessaging.DefaultInstance);
                services.AddScoped<IPushSender, FcmPushSender>();
                fcmReady = true;
                Console.WriteLine("[Push] FCM configuré : FcmPushSender actif (les push seront envoyés).");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Push] FCM configuré mais INVALIDE ({ex.GetType().Name} : {ex.Message}). " +
                    "Repli sur NullPushSender (aucun push). Vérifier le JSON du compte de service " +
                    "— clé privée base64 valide et échappement des « \\n » (simple, pas double).");
            }
        }
        if (!fcmReady)
        {
            // EN PRODUCTION, ON REFUSE DE DÉMARRER. LE PUSH EST PORTEUR, PAS
            // DÉCORATIF.
            if (IsProduction(configuration))
            {
                throw new InvalidOperationException(
                    "Aucun émetteur de notifications push utilisable en production. "
                    + (fcm.IsConfigured
                        ? "Notifications:Fcm est renseigné mais le compte de service n'a pas pu être chargé "
                          + "(voir la cause exacte dans le journal [Push] ci-dessus)."
                        : "Notifications:Fcm n'est pas configuré.")
                    + " Les offres de course aux livreurs et les avancements de commande passent par ce canal : "
                    + "le service refuse de démarrer plutôt que de les perdre en silence.");
            }

            services.AddScoped<IPushSender, NullPushSender>();
            // DIAGNOSTIC (uniquement si RIEN n'était configuré ; le cas « configuré
            // mais invalide » a déjà loggé sa propre cause dans le catch
            // ci-dessus).
            if (!fcm.IsConfigured)
            {
                Console.WriteLine(
                    $"[Push] FCM NON configuré : NullPushSender actif (aucun push). " +
                    $"Path='{fcm.ServiceAccountPath}', JsonInline={( string.IsNullOrWhiteSpace(fcm.ServiceAccountJson) ? "non" : "oui")}, " +
                    $"FichierExiste={(!string.IsNullOrWhiteSpace(fcm.ServiceAccountPath) && File.Exists(fcm.ServiceAccountPath) ? "oui" : "non")}.");
            }
        }

        // E-MAIL. IL N'Y EN AVAIT AUCUN — ET CE VIDE AVAIT PRODUIT UNE FAILLE
        // CRITIQUE.
        var email = BindEmailOptions(configuration);
        services.AddSingleton(email);
        services.AddSingleton<IAccountLinkBuilder, AccountLinkBuilder>();

        if (email.IsConfigured)
        {
            services.AddHttpClient(ResendEmailSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddScoped<IEmailSender, ResendEmailSender>();
        }
        else if (IsProduction(configuration))
        {
            // REFUS DE DÉMARRER. C'est volontairement brutal.
            throw new InvalidOperationException(
                "Notifications:Email n'est pas configuré (ApiKey, From, AppBaseUrl) alors que "
                + "l'environnement est Production. Sans canal e-mail, la vérification d'adresse et "
                + "la réinitialisation de mot de passe sont IMPOSSIBLES : les utilisateurs qui "
                + "oublient leur mot de passe seront définitivement bloqués. "
                + "Renseigner les secrets dans le vault, puis redéployer.");
        }
        else
        {
            // Développement : l'e-mail est écrit dans la console (jeton compris),
            // pour que le flux complet reste praticable en local.
            services.AddScoped<IEmailSender, DevelopmentEmailSender>();
            Console.WriteLine(
                "[E-mail] Notifications:Email NON configuré : DevelopmentEmailSender actif "
                + "(les e-mails sont écrits dans la console, PAS envoyés).");
        }

        // SMS. IL N'Y EN AVAIT AUCUN — ET `SMS` ÉTAIT LE CANAL OTP PAR DÉFAUT.
        var sms = BindSmsOptions(configuration);
        services.AddSingleton(sms);

        if (sms.IsConfigured)
        {
            // REFUS DE DÉMARRER, ET C'EST LE CAS LE PLUS SUBTIL DES TROIS.
            throw new InvalidOperationException(
                "Notifications:Sms est configuré, mais AUCUN adaptateur SMS de production "
                + "n'est enregistré dans ce dépôt : le fournisseur n'a pas encore été "
                + "choisi (voir l'en-tête d'ISmsSender). Écrire la classe qui implémente "
                + "ISmsSender pour le fournisseur retenu et l'enregistrer ici, ou retirer "
                + "la section Notifications:Sms.");
        }

        if (IsProduction(configuration))
        {
            // REFUS DE DÉMARRER. Même raisonnement que pour l'e-mail.
            throw new InvalidOperationException(
                "Notifications:Sms n'est pas configuré (ApiKey, SenderId, BaseUrl) alors que "
                + "l'environnement est Production. `SMS` est le canal OTP PAR DÉFAUT : sans "
                + "lui, aucun code de connexion n'atteint son destinataire, et l'échec est "
                + "totalement silencieux. Choisir un fournisseur, écrire son adaptateur, "
                + "renseigner les secrets dans le vault — ou retirer `SMS` de MfaChannels.All "
                + "côté identity si le canal est abandonné.");
        }

        // Développement : le SMS est écrit dans la console (code compris), pour que
        // le flux OTP complet reste praticable en local.
        services.AddScoped<ISmsSender, DevelopmentSmsSender>();
        Console.WriteLine(
            "[SMS] Notifications:Sms NON configuré : DevelopmentSmsSender actif "
            + "(les SMS sont écrits dans la console, PAS envoyés).");

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        // LITIGES. À l'ouverture, l'ADMIN est alerté (sinon rien ne remonte hors
        // console) ; à la résolution, l'ACHETEUR apprend la décision.
        services.AddScoped<AdminNotificationTarget>();

    }

    private static SmsOptions BindSmsOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notifications:Sms");
        return new SmsOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            SenderId = section["SenderId"] ?? string.Empty,
            BaseUrl = section["BaseUrl"] ?? string.Empty,
        };
    }

    private static EmailOptions BindEmailOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notifications:Email");
        return new EmailOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            From = section["From"] ?? string.Empty,
            AppBaseUrl = section["AppBaseUrl"] ?? string.Empty,
        };
    }

    /// <summary>Sommes-nous en production ?</summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        return EnvironnementDeploiement.EstProduction(configuration);
    }

    private static FcmOptions BindFcmOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notifications:Fcm");
        return new FcmOptions
        {
            ServiceAccountJson = section["ServiceAccountJson"] ?? string.Empty,
            ServiceAccountPath = section["ServiceAccountPath"] ?? string.Empty,
        };
    }
}
