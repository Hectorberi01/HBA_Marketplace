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
using HBA.Shipping.Contracts.IntegrationEvents;
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

/// <summary>Enregistre le module Notifications : DbContext, repository, dispatcher et consumers fan-out.</summary>
public sealed class NotificationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Notifications";

    public Assembly ApplicationAssembly => typeof(ListMyNotificationsQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/). Il etait branche par le
        // socle pour les vingt-six services a la fois ; il l'est desormais ici.
        services.AjouterCacheCommunicationNotifications(configuration);

        // LES SONDES DE CE SERVICE (Observability/). Jusqu'ici seule la base
        // etait verifiee : un service dont le consommateur Kafka etait mort
        // repondait « ready », et le deploiement individuel le croyait sain.
        services.AjouterObservabiliteCommunicationNotifications(configuration);

        // « Default », ET NON « Marketplace ».
        //
        // Ce module vient du monolithe, où la chaîne s'appelait « Marketplace » —
        // une seule base pour vingt-neuf modules. Les treize services nomment la
        // leur « Default », et le compose ne renseigne que
        // `CONNECTIONSTRINGS__DEFAULT`.
        //
        // Le nom d'origine avait survécu au déménagement. Résultat :
        // communication-service compilait, démarrait, puis levait sur une clé de
        // configuration que personne n'avait jamais eu l'intention de fournir.
        // C'était le DERNIER des dix-huit installeurs à ne pas suivre la
        // convention.
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc
        // hors de cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieCommunicationNotifications()`, que le composition root peut oublier.
        // Un oubli ne casserait rien de visible — le service demarre et n'emet
        // plus rien. Cette garde, elle, est enregistree ici : elle doit exister
        // quand ce qu'elle verifie est absent.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName)));

        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();

        // Socle du §5 et du §19.5.
        // LE MAGASIN ET SON PURGEUR, EN UN SEUL GESTE.
        //
        // `ExpiresAtUtc` existait depuis le début, avec son index de purge, et
        // aucune ligne de code ne la lisait : une réservation inachevée bloquait
        // sa clé pour toujours (audit 1.8). Les deux enregistrements sont
        // désormais indissociables — voir `IdempotencyRegistration` pour la
        // raison, qui tient en une phrase : un huitième service qui ne copierait
        // que la première ligne n'aurait jamais de purge, sans rien signaler.
        services.AjouterIdempotenceCommunicationNotifications();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<NotificationDispatcher>();

        // Push (FCM) : si un compte de service Firebase est configuré, on branche
        // l'envoi réel ; sinon un adaptateur no-op (aucun push, mais tout compile/tourne).
        var fcm = BindFcmOptions(configuration);
        services.AddSingleton(fcm);
        var fcmReady = false;
        if (fcm.IsConfigured)
        {
            // Le push est OPTIONNEL : un compte de service FCM MALFORMÉ (clé privée
            // invalide, JSON tronqué, `\n` mal échappés…) ne doit PAS abattre toute la
            // plateforme. On tente de le brancher ; en cas d'échec on DÉGRADE proprement
            // vers NullPushSender en traçant la cause, au lieu de crasher au démarrage.
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
            // ═════════════════════════════════════════════════════════════════════
            // EN PRODUCTION, ON REFUSE DE DÉMARRER. LE PUSH EST PORTEUR, PAS DÉCORATIF.
            //
            // On pourrait croire qu'un push manquant n'est qu'un confort en moins.
            // Sur cette plateforme, non : l'offre de course part au livreur par
            // notification. Un `NullPushSender` en production, c'est un dispatch qui
            // ne propose plus rien, des commandes qui n'avancent plus, et une cause
            // qu'on ne trouve qu'en relisant les journaux de démarrage.
            //
            // Ce refus couvre les DEUX cas d'échec : FCM non configuré, et FCM
            // configuré mais invalide (JSON de compte de service illisible). Le
            // second est le plus insidieux — la configuration a l'air complète, et
            // seul le `catch` ci-dessus sait qu'elle ne l'est pas.
            //
            // Même règle que l'e-mail quelques lignes plus bas, et que les
            // passerelles de paiement : hors production, on simule et on le DIT ;
            // en production, on refuse.
            // ═════════════════════════════════════════════════════════════════════
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
            // DIAGNOSTIC (uniquement si RIEN n'était configuré ; le cas « configuré mais
            // invalide » a déjà loggé sa propre cause dans le catch ci-dessus).
            if (!fcm.IsConfigured)
            {
                Console.WriteLine(
                    $"[Push] FCM NON configuré : NullPushSender actif (aucun push). " +
                    $"Path='{fcm.ServiceAccountPath}', JsonInline={( string.IsNullOrWhiteSpace(fcm.ServiceAccountJson) ? "non" : "oui")}, " +
                    $"FichierExiste={(!string.IsNullOrWhiteSpace(fcm.ServiceAccountPath) && File.Exists(fcm.ServiceAccountPath) ? "oui" : "non")}.");
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // E-MAIL. IL N'Y EN AVAIT AUCUN — ET CE VIDE AVAIT PRODUIT UNE FAILLE CRITIQUE.
        //
        // Aucun IEmailSender n'existait dans le dépôt. Conséquences en chaîne :
        //   • l'e-mail de vérification n'est jamais parti (l'événement n'avait aucun
        //     consommateur) ;
        //   • le jeton de réinitialisation n'avait nulle part où aller, alors il a été
        //     renvoyé DANS LA RÉPONSE HTTP d'un endpoint anonyme. N'importe qui prenait
        //     n'importe quel compte, administrateurs compris.
        //
        // Une capacité manquante finit toujours par être contournée. Le contournement est
        // rarement sûr.
        // ═════════════════════════════════════════════════════════════════════════
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
            //
            // Sans e-mail en production : aucun compte ne peut être vérifié, et surtout
            // AUCUN mot de passe oublié ne peut être récupéré. Les utilisateurs se
            // retrouvent enfermés dehors, définitivement, et l'exploitant ne s'en aperçoit
            // que par les plaintes.
            //
            // Un adaptateur silencieux ferait « tourner » la plateforme dans cet état. Un
            // démarrage refusé se remarque tout de suite — et se corrige en une variable.
            throw new InvalidOperationException(
                "Notifications:Email n'est pas configuré (ApiKey, From, AppBaseUrl) alors que "
                + "l'environnement est Production. Sans canal e-mail, la vérification d'adresse et "
                + "la réinitialisation de mot de passe sont IMPOSSIBLES : les utilisateurs qui "
                + "oublient leur mot de passe seront définitivement bloqués. "
                + "Renseigner les secrets dans le vault, puis redéployer.");
        }
        else
        {
            // Développement : l'e-mail est écrit dans la console (jeton compris), pour que
            // le flux complet reste praticable en local. Impossible en production — la
            // garde ci-dessus s'est déclenchée avant.
            services.AddScoped<IEmailSender, DevelopmentEmailSender>();
            Console.WriteLine(
                "[E-mail] Notifications:Email NON configuré : DevelopmentEmailSender actif "
                + "(les e-mails sont écrits dans la console, PAS envoyés).");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // SMS. IL N'Y EN AVAIT AUCUN — ET `SMS` ÉTAIT LE CANAL OTP PAR DÉFAUT.
        //
        // `MfaChannels.All` vaut `[SMS, EMAIL]` et `IssueOtpChallengeCommand` retombe
        // sur `SMS` quand l'appelant ne précise rien. Le dépôt ne portait pourtant
        // aucun fournisseur SMS : sur une plateforme mobile béninoise, le canal qui
        // compte était le seul sans implémentation.
        //
        // AUCUN ADAPTATEUR DE PRODUCTION N'EST ENREGISTRÉ ICI, ET C'EST VOULU.
        // Le fournisseur reste à choisir — c'est un contrat commercial, un compte
        // opérateur et un expéditeur à faire homologuer, pas une décision technique.
        // Écrire un adaptateur pour un fournisseur arbitraire aurait produit du code
        // plausible et jamais exécuté. Voir l'en-tête d'`ISmsSender`.
        //
        // Quand le fournisseur sera retenu : une classe qui implémente `ISmsSender`,
        // et la ligne `services.AddScoped<ISmsSender, XSmsSender>();` dans la branche
        // « configuré » ci-dessous.
        // ═════════════════════════════════════════════════════════════════════════
        var sms = BindSmsOptions(configuration);
        services.AddSingleton(sms);

        if (sms.IsConfigured)
        {
            // REFUS DE DÉMARRER, ET C'EST LE CAS LE PLUS SUBTIL DES TROIS.
            //
            // La configuration est renseignée — donc quelqu'un a ouvert un compte,
            // payé, et croit que les SMS partent. Retomber ici sur l'adaptateur de
            // développement écrirait les codes dans la console d'un serveur, et
            // personne ne s'en apercevrait avant les plaintes. Une configuration qui
            // désigne un fournisseur sans adaptateur est une contradiction : il vaut
            // mieux la dire à voix haute.
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
            //
            // Sans SMS en production, tout défi OTP demandé sur le canal par défaut
            // est émis, stocké, facturé en base — et n'atteint personne. L'utilisateur
            // voit un écran de saisie qui n'aboutira jamais, et l'exploitant ne le
            // découvre que par les plaintes. Un démarrage refusé se remarque tout de
            // suite.
            throw new InvalidOperationException(
                "Notifications:Sms n'est pas configuré (ApiKey, SenderId, BaseUrl) alors que "
                + "l'environnement est Production. `SMS` est le canal OTP PAR DÉFAUT : sans "
                + "lui, aucun code de connexion n'atteint son destinataire, et l'échec est "
                + "totalement silencieux. Choisir un fournisseur, écrire son adaptateur, "
                + "renseigner les secrets dans le vault — ou retirer `SMS` de MfaChannels.All "
                + "côté identity si le canal est abandonné.");
        }

        // Développement : le SMS est écrit dans la console (code compris), pour que le
        // flux OTP complet reste praticable en local. Impossible en production — les
        // deux gardes ci-dessus se sont déclenchées avant.
        services.AddScoped<ISmsSender, DevelopmentSmsSender>();
        Console.WriteLine(
            "[SMS] Notifications:Sms NON configuré : DevelopmentSmsSender actif "
            + "(les SMS sont écrits dans la console, PAS envoyés).");

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        // LITIGES. À l'ouverture, l'ADMIN est alerté (sinon rien ne remonte hors console) ;
        // à la résolution, l'ACHETEUR apprend la décision.
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
