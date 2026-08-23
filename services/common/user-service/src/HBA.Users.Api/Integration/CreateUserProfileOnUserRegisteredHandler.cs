using HBA.Identity.Contracts;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Application.Abstractions;
using HBA.Users.Application.Profiles;
using MediatR;

namespace HBA.Users.Api.Integration;

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
/// IL VIT DANS LE PROJET Api, ET NON DANS Application.
///
/// Il connaît les DEUX mondes. `UsersBoundaryTests` interdit au module User de
/// dépendre d'Identity, Contracts compris, et le motif est écrit dans ce test :
/// « un appel à IIdentityModuleApi pour vérifier que l'utilisateur existe
/// paraîtrait raisonnable et recréerait pourtant le couplage que ce déplacement
/// vient de défaire ».
///
/// La composition root, elle, a le droit de tout connaître. Le couplage existe —
/// il faut bien que quelqu'un relie l'inscription au profil — mais il est ISOLÉ
/// dans un fichier qu'on supprime pour détacher les deux mondes.
///
/// L'ÉVÉNEMENT NE PORTE PAS LE NOM DE FAMILLE. ON RELIT LE COMPTE.
///
/// `UserRegisteredIntegrationEvent` transporte l'identifiant, l'e-mail et le
/// PRÉNOM — il a été taillé pour Notifications, qui n'a besoin que du prénom
/// pour dire « Bonjour Awa ».
///
/// Deux réponses possibles : élargir l'événement, ou relire le compte. On relit.
/// Élargir un événement d'intégration pour le confort d'un consommateur est le
/// premier pas vers un événement qui transporte tout l'agrégat, et chaque champ
/// ajouté devient un engagement envers tous les autres consommateurs, présents
/// et futurs. Une lecture de plus à l'inscription — opération rare s'il en est —
/// coûte infiniment moins cher.
///
/// La relecture passe désormais par gRPC et non par la mémoire : c'est la seule
/// différence avec la version monolithique.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CreateUserProfileOnUserRegisteredHandler: IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
    private const string ConsumerName = "user-service.identity-user-registered";

    private readonly ISender _sender;
    private readonly IIdentityModuleApi _identity;
    private readonly IConsumerInbox _inbox;
    private readonly IUsersUnitOfWork _unitOfWork;
    private readonly ILogger<CreateUserProfileOnUserRegisteredHandler> _logger;

    public CreateUserProfileOnUserRegisteredHandler(
        ISender sender,
        IIdentityModuleApi identity,
        IConsumerInbox inbox,
        IUsersUnitOfWork unitOfWork,
        ILogger<CreateUserProfileOnUserRegisteredHandler> logger)
    {
        _sender = sender;
        _identity = identity;
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

        var compte = await _identity.GetUserAsync(integrationEvent.UserId, cancellationToken);

        if (compte is null)
        {
            // Le compte a disparu entre la publication et la consommation — une
            // suppression immédiate, ou une base restaurée entre-temps. On ne
            // crée pas un profil orphelin : il réapparaîtrait dans les listes
            // d'administration sans compte derrière.
            _logger.LogWarning(
                "Profil non créé pour le compte {UserId} : compte introuvable à la consommation "
                + "de UserRegistered.",
                integrationEvent.UserId);

            return;
        }

        // La commande est IDEMPOTENTE : Kafka livre au moins une fois, et un
        // rejeu après redémarrage rappellerait ce gestionnaire. Un profil déjà
        // présent n'est PAS écrasé — sans quoi le rejeu annulerait toute
        // correction de nom faite depuis.
        var result = await _sender.Send(
            new CreateUserProfileCommand(compte.Id, compte.FirstName, compte.LastName),
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
