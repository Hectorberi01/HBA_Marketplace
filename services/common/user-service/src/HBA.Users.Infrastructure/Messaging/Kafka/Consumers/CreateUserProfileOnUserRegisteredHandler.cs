using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Application.Abstractions;
using HBA.Users.Application.Profiles;
using MediatR;
using Microsoft.Extensions.Logging;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Users.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN COMPTE EST CRÉÉ → UN PROFIL EST CRÉÉ.
///
/// CE CONSOMMATEUR N'AVAIT PAS SURVÉCU À L'EXTRACTION.
///
/// Dans le monolithe il vivait dans la composition root
/// (`Marketplace.Api/Integration`). En sortant user-service, on a emporté le
/// module — domaine, application, persistance, routes — et laissé le fichier qui
/// le RELIAIT à Identity. identity-service publiait donc consciencieusement
/// `UserRegisteredIntegrationEvent` dans Kafka, et personne ne l'écoutait.
///
/// Le symptôme observé : un compte apparaît dans `identity.users`, aucune ligne
/// dans `users.profiles`. Rien n'échoue, rien ne journalise — l'événement part,
/// se pose sur le sujet, et n'a pas de destinataire.
///
/// IL VIT DANS `Infrastructure/Messaging/Kafka/Consumers/`.
///
/// Il vivait dans le composition root (`Api/Integration`), au motif d'une
/// frontière `UsersBoundaryTests` qui interdisait au module User de connaître
/// Identity. CE TEST N'EXISTE PAS DANS LE DÉPÔT : la garde était un commentaire.
/// Le couplage est désormais assumé, et confiné à ce dossier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// IL NE RELIT PLUS LE COMPTE PAR gRPC. LE RAISONNEMENT PRÉCÉDENT ÉTAIT JUSTE, ET
///     LE VERDICT ÉTAIT FAUX.
///
/// Ce commentaire disait : « Élargir un événement d'intégration pour le confort
/// d'un consommateur est le premier pas vers un événement qui transporte tout
/// l'agrégat […] Une lecture de plus à l'inscription — opération rare s'il en
/// est — coûte infiniment moins cher. »
///
/// L'argument sur la discipline des contrats reste bon. Le calcul du coût, lui,
/// ne comptait que le temps de la lecture. Il manquait le reste :
///
///   - Un APPEL SYNCHRONE DANS UN CONSOMMATEUR fait dépendre le traitement d'un
///     événement de la disponibilité d'un AUTRE service. L'asynchrone existait
///     précisément pour découpler les deux ; l'appel le rétablit à l'intérieur.
///   - L'échec est INVISIBLE. Une route HTTP qui échoue rend 500 et se voit. Ici,
///     le gestionnaire lève, le consommateur réessaie, puis journalise
///     « ÉVÉNEMENT ABANDONNÉ » dans un flux que personne ne regarde.
///   - CE N'EST PAS UNE HYPOTHÈSE. `Internal:PrivateKey` encodée en SEC1 au lieu
///     de PKCS#8 : la signature échouait, l'appel échouait, le profil n'était
///     jamais créé. `identity.users` avait deux lignes, `users.user_profiles`
///     zéro, et l'interface d'administration ne montrait rien d'anormal.
///
/// `UserRegisteredIntegrationEvent` porte maintenant `LastName`. Ce service ne
/// signe plus aucun appel sortant vers identity-service, et peut donc traiter une
/// inscription pendant qu'identity-service redémarre, ou est en panne.
///
/// CE QU'ON A PERDU, ET IL FAUT LE SAVOIR. L'ancienne relecture servait aussi de
/// garde : un compte supprimé entre la publication et la consommation rendait
/// `null`, et on s'abstenait de créer un profil orphelin. Cette garde n'existe
/// plus. Un profil peut donc naître pour un compte déjà parti ;
/// `UserAnonymized` le purge derrière, mais l'ordre des deux événements n'est
/// garanti que par la clé de partition, pas par le code.
///
/// `LastName` EST NULLABLE. Les messages déjà sur le sujet n'en portent pas : le
/// rejeu prévu pour rattraper les profils manquants créera des profils sans nom
/// de famille. C'est le prix du rattrapage, et il est préférable à un profil
/// absent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CreateUserProfileOnUserRegisteredHandler: IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
    private const string ConsumerName = "user-service.identity-user-registered";

    private readonly ISender _sender;
    private readonly IConsumerInbox _inbox;
    private readonly IUsersUnitOfWork _unitOfWork;
    private readonly ILogger<CreateUserProfileOnUserRegisteredHandler> _logger;

    public CreateUserProfileOnUserRegisteredHandler(
        ISender sender,
        IConsumerInbox inbox,
        IUsersUnitOfWork unitOfWork,
        ILogger<CreateUserProfileOnUserRegisteredHandler> logger)
    {
        _sender = sender;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(UserRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // ═════════════════════════════════════════════════════════════════════
        // GARDE D'IDEMPOTENCE DU §19.5 — ET CE QU'ELLE APPORTE VRAIMENT ICI.
        //
        // Ce gestionnaire était DÉJÀ protégé du rejeu : `CreateUserProfileCommand`
        // n'écrase pas un profil existant. L'inbox n'est donc pas ce qui empêche le
        // double profil — elle évite l'appel gRPC et le travail inutile d'un rejeu,
        // et surtout elle installe le motif là où il sera load-bearing : un
        // gestionnaire qui crédite un wallet ou débite un stock n'a AUCUNE
        // idempotence naturelle, et sans cette garde un simple rebalancement de
        // partition Kafka crédite deux fois.
        //
        // L'ATOMICITÉ N'EST PAS COMPLÈTE, ET IL FAUT LE SAVOIR.
        //
        // Le §19.5 veut la trace et l'effet métier dans la MÊME transaction. Ici,
        // `_sender.Send(...)` ouvre et valide la sienne, puis la trace est écrite
        // ensuite. Une panne entre les deux rejouerait l'événement — sans dommage,
        // la commande étant idempotente. Fermer complètement la fenêtre suppose de
        // faire descendre l'inbox dans le handler de commande, ce qui vaut la peine
        // pour les flux financiers et pas pour celui-ci.
        // ═════════════════════════════════════════════════════════════════════
        if (await _inbox.HasProcessedAsync(integrationEvent.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug("Événement {EventId} déjà traité par {Consumer} : ignoré.",integrationEvent.Id, ConsumerName);
            return;
        }

        // La commande est IDEMPOTENTE : Kafka livre au moins une fois, et un
        // rejeu après redémarrage rappellerait ce gestionnaire. Un profil déjà
        // présent n'est PAS écrasé — sans quoi le rejeu annulerait toute
        // correction de nom faite depuis.
        var result = await _sender.Send(
            new CreateUserProfileCommand(
                integrationEvent.UserId,
                integrationEvent.FirstName,
                integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            // ON LÈVE, CONTRAIREMENT AUX GESTIONNAIRES DE RÔLES.
            //
            // La distinction est délibérée. `BusinessRoleGrant` ne lève jamais :
            // un rôle absent en base ne se répare pas en réessayant, et rejouer
            // indéfiniment ne ferait que du bruit.
            //
            // Ici c'est l'inverse. Avaler l'échec laisserait le compte SANS
            // PROFIL définitivement — aucune route ne permet d'en créer un après
            // coup. Son nom n'apparaîtrait nulle part, et les e-mails qui le
            // nomment partiraient incomplets, pour toujours.
            //
            // La commande étant idempotente, lever ne coûte qu'une nouvelle
            // tentative.
            _logger.LogError(
                "Profil NON créé pour le compte {UserId} — {Code} : {Message}",
                integrationEvent.UserId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"Création du profil impossible pour le compte {integrationEvent.UserId} : "
                + $"{result.Error.Code} — {result.Error.Message}");
        }

        // Trace de consommation. Écrite APRÈS le succès seulement : la marquer avant
        // ferait considérer comme traité un événement dont l'effet métier a échoué,
        // et le rejeu — la seule chance de réparation — n'aurait jamais lieu.
        await _inbox.MarkProcessedAsync(
            integrationEvent.Id,
            ConsumerName,
            "identity.user.registered",
            HbaRequestContext.Current.CorrelationId,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
