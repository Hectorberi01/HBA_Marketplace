using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Kafka;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Api.Messaging.Kafka.Consumers;

namespace HBA.Users.Api.Messaging.Kafka.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TOUT CE QUE user-service ÉCOUTE, DÉCLARÉ AU MÊME ENDROIT.
///
/// POURQUOI CE MODULE EXISTE. Les consommateurs de la plateforme vivaient dans
/// SIX conventions différentes selon le service — `Application/EventHandlers`,
/// `Infrastructure/Integration`, `Api/Integration`, `Application/Abstractions/
/// EventHandlers`, `Application/Earnings`, et deux fichiers posés à la racine
/// d'`Api`. Chercher « qui écoute quoi » supposait de connaître la convention du
/// service qu'on ouvrait.
///
/// LA LISTE DES SUJETS ET LES GESTIONNAIRES SONT DANS LE MÊME FICHIER, ET C'EST
///     LA SEULE PROTECTION QUI EXISTE.
///
/// Un gestionnaire enregistré dont le sujet n'est pas déclaré ne sera JAMAIS
/// appelé, sans erreur ni avertissement — l'événement n'arrive simplement pas.
/// Aucun compilateur ne relie les deux. Les mettre côte à côte fait qu'on ne peut
/// pas modifier l'un sans voir l'autre ; c'est faible, et c'est tout ce qu'on a.
///
/// CE MODULE VIT DANS `Api` ET NON DANS `Infrastructure`, CONTRAIREMENT À LA
///     CONVENTION VISÉE.
///
/// `HBA.Users.Infrastructure` n'a pas le droit de connaître Identity : c'est la
/// frontière que `Program.cs` documente déjà — « l'enregistrement est fait dans
/// le composition root, PAS dans `UsersModuleInstaller` ». Or ces trois
/// gestionnaires consomment des contrats d'Identity, et l'un d'eux le relit par
/// gRPC. Les descendre dans `Infrastructure` ferait référencer
/// `HBA.Identity.Contracts` par le module User — exactement le couplage que
/// l'extraction du monolithe a défait.
///
/// La composition root, elle, a le droit de tout connaître. Le couplage existe,
/// il est ISOLÉ ici, et il se supprime en supprimant ce dossier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class MessagerieUsers
{
    /// <summary>
    /// LES SUJETS, NOMMÉS EN ENTIER.
    ///
    /// user-service consomme TROIS types d'événement, tous produits par
    /// identity-service. Il s'abonnait pourtant aux VINGT sujets de la plateforme,
    /// faute d'un code qui remplisse `SubscribeTopics` — voir `AbonnementsKafka`.
    ///
    /// Le jour où ce service écoutera un second domaine, la ligne à ajouter est
    /// ici, à côté du gestionnaire qui la justifie.
    /// </summary>
    private static readonly string[] Sujets = ["service.identity.v1"];

    /// <summary>
    /// Déclare les abonnements et les gestionnaires. Appelée par `Program.cs`.
    /// </summary>
    public static IServiceCollection AjouterMessagerieUsers(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));

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
