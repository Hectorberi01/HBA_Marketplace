using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Users.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Users.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Producers;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Users.Infrastructure.Messaging.Kafka;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MODULE KAFKA DE user-service — UN SEUL POINT D'ENTRÉE.
///
/// POURQUOI CE MODULE EXISTE. Les consommateurs de la plateforme vivaient dans
/// SIX conventions différentes selon le service — `Application/EventHandlers`,
/// `Infrastructure/Integration`, `Api/Integration`, `Application/Abstractions/
/// EventHandlers`, `Application/Earnings`, et deux fichiers posés à la racine
/// d'`Api`. Chercher « qui écoute quoi » supposait de connaître la convention du
/// service qu'on ouvrait.
///
/// LA FORME DU DOSSIER, ET CE QU'ELLE VEUT DIRE.
///
///   Configuration/  les sujets écoutés, et la garde de câblage
///   Consumers/      ce qu'on écoute
///   Producers/      ce qu'on publie — DÉCLARÉ, jamais envoyé d'ici
///   Outbox/         le câblage du chemin de sortie
///   Inbox/          le câblage de la garde anti-doublon
///
/// LA LIGNE DE PARTAGE, VALABLE POUR LES DIX-HUIT SERVICES SUIVANTS : CE DOSSIER
///     PORTE LA POLITIQUE DU SERVICE, LE SOCLE PARTAGÉ PORTE LE TYPE ET LE
///     PROTOCOLE.
///
/// `Outbox/` et `Inbox/` contiennent le geste d'enregistrement, PAS une copie de
/// `OutboxMessage` ni de `ConsumerInboxEntry`. Ces deux-là sont des entités EF
/// dont les tables sont créées par les migrations de dix-huit services et
/// remplies par `ModuleDbContext.SaveChangesAsync` ; les dupliquer par service
/// forkerait le contrat de la table sans que rien ne le signale. Le détail est
/// écrit dans `Outbox/OutboxUsers`.
///
/// CE QUI N'EST PAS ICI, ET POURQUOI.
///
/// `Serialization/` n'existe pas dans ce service : il n'a aucun convertisseur
/// propre et utilise celui de `HBA.Shared.Infrastructure.Kafka`. Un dossier vide
/// se lirait comme une promesse tenue ailleurs ; il sera créé par le premier
/// service qui a vraiment un convertisseur à y mettre.
///
/// `Interceptors/` non plus : la corrélation et le `traceparent` sont déjà portés
/// par l'enveloppe, posés par `KafkaIntegrationEventPublisher` et relus par
/// `KafkaIntegrationEventConsumer`. Un intercepteur local ferait une SECONDE
/// implémentation du même contrat — la cause de chacune des pannes de la semaine.
///
/// L'IDEMPOTENCE reste dans `UsersModuleInstaller` : elle sert aussi les routes
/// HTTP annotées `AllowIdempotency()`, qui n'ont rien à voir avec Kafka.
///
/// CE MODULE VIT DANS `Infrastructure`, ET IL A FALLU OUVRIR UNE FRONTIÈRE POUR
///     L'Y METTRE.
///
/// Les commentaires de ce service citent `UsersBoundaryTests`, qui interdisait au
/// module User de dépendre d'Identity. CE TEST N'EXISTE PAS DANS LE DÉPÔT : la
/// garde était un commentaire, jamais un contrôle — `HBA.Users.Api` référençait
/// déjà `HBA.Identity.Contracts` sans que rien ne s'y oppose. Le couplage est
/// confiné à ce dossier et se supprime en le supprimant. Si la frontière doit
/// redevenir opposable, c'est un TEST D'ARCHITECTURE qu'il faut écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Branche toute la messagerie du service. Appelée par `Program.cs` ; son
    /// absence est détectée au démarrage par `GardeDeCablage`.
    /// </summary>
    public static IServiceCollection AjouterMessagerieUsers(this IServiceCollection services)
    {
        // CE QU'ON PUBLIE EST VÉRIFIÉ AVANT CE QU'ON ÉCOUTE.
        //
        // Un événement sans `[HbaEvent]` part quand même, sous un nom de repli, et
        // le consommateur d'en face le rejette en silence. Échouer ici coûte un
        // démarrage ; le découvrir en face coûte des semaines. Voir
        // `EvenementsPublies`.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsUsers();
        services.AjouterOutboxUsers();
        services.AjouterInboxUsers();

        // Inscription → création du profil.
        services.AddScoped<
            IIntegrationEventHandler<UserRegisteredIntegrationEvent>,
            CreateUserProfileOnUserRegisteredHandler>();

        // Le compte change de nom → le profil suit.
        services.AddScoped<
            IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>,
            RenameUserProfileOnIdentityProfileUpdatedHandler>();

        // CELUI-CI EST UNE OBLIGATION LÉGALE, PAS UN CONFORT.
        //
        // Sans lui, un compte supprimé laisse le carnet d'adresses de son
        // titulaire en base, indéfiniment, sans que rien ne signale qu'il aurait
        // dû partir.
        services.AddScoped<
            IIntegrationEventHandler<UserAnonymizedIntegrationEvent>,
            PurgeUserDataOnAccountAnonymizedHandler>();

        return services;
    }
}
