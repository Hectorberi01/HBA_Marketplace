using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Application.Profiles;
using MediatR;

namespace HBA.Users.Api.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COMPTE CHANGE → LE PROFIL SUIT.
///
/// Deux adaptateurs, une même raison d'être : user-service tient des données
/// personnelles dont le cycle de vie est décidé par identity-service, et les deux
/// n'ont pas le droit de se connaître. Seule la composition root les relie.
///
/// PERDUS À L'EXTRACTION, COMME LA CRÉATION DE PROFIL.
///
/// Ils vivaient dans `Marketplace.Api/Integration`. Le module User est parti dans
/// son service, ces deux ponts sont restés dans le monolithe. Conséquences,
/// toutes deux silencieuses :
///
///   • un utilisateur corrige son nom → Identity le change, le profil garde
///     l'ancien, et plus rien ne les réconcilie ;
///   • un compte est supprimé → Identity l'anonymise, et le CARNET D'ADRESSES
///     de son titulaire reste en base indéfiniment.
///
/// Le second est une obligation légale non tenue, sans le moindre signal.
///
/// POURQUOI UN ÉVÉNEMENT PLUTÔT QUE DEUX APPELS DEPUIS LE BFF.
///
/// Le BFF pourrait envoyer deux commandes de suite — une à Identity, une à User.
/// C'est plus court à lire, et c'est faux : la seconde peut échouer après la
/// première, et rien ne la rejouerait. Le client verrait une erreur, réessaierait,
/// et repartirait avec un profil désynchronisé sans jamais le savoir.
///
/// Le prix est une cohérence différée de quelques centaines de millisecondes sur
/// un nom affiché — largement préférable à une divergence permanente.
///
/// LE SENS DE LA COPIE S'INVERSERA.
///
/// Aujourd'hui Identity fait autorité sur le nom, parce que dix-sept appelants
/// lisent encore `UserSummary`. Quand ils liront le profil, c'est User qui écrira
/// et Identity qui perdra ses colonnes — ce fichier disparaîtra alors avec
/// l'événement qu'il consomme. Il est transitoire par construction.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RenameUserProfileOnIdentityProfileUpdatedHandler
    : IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<RenameUserProfileOnIdentityProfileUpdatedHandler> _logger;

    public RenameUserProfileOnIdentityProfileUpdatedHandler(
        ISender sender, ILogger<RenameUserProfileOnIdentityProfileUpdatedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserProfileUpdatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new RenameUserProfileCommand(
                integrationEvent.UserId, integrationEvent.FirstName, integrationEvent.LastName),
            cancellationToken);

        if (result.IsFailure)
        {
            // DEUX TRAITEMENTS, ET LA DISTINCTION EST LE FOND DU SUJET.
            //
            // « profil introuvable » est un cas ATTENDU et DÉFINITIF : le compte
            // a été créé avant que le pont de création n'existe. Rejouer
            // échouerait exactement pareil, indéfiniment — une alerte pour un
            // incident qui n'en est pas un. On journalise et on passe.
            //
            // TOUT LE RESTE est une panne : base indisponible, conflit de
            // concurrence, validation inattendue. Là il faut LEVER, sinon le
            // message est réputé traité et le nom reste divergent pour toujours.
            if (result.Error.Code == "users.profile.not_found")
            {
                _logger.LogWarning(
                    "Profil non renommé pour le compte {UserId} : aucun profil "
                    + "(compte antérieur au branchement du pont de création).",
                    integrationEvent.UserId);

                return;
            }

            _logger.LogError(
                "Profil NON renommé pour le compte {UserId} — {Code} : {Message}",
                integrationEvent.UserId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"Renommage du profil impossible pour le compte {integrationEvent.UserId} : "
                + $"{result.Error.Code} — {result.Error.Message}");
        }
    }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COMPTE EST SUPPRIMÉ → LE PROFIL ET LES ADRESSES DISPARAISSENT.
///
/// `User.Anonymize` nettoie méticuleusement le compte : nom, e-mail, téléphone,
/// secret MFA, jetons. Il ne peut nettoyer que ce qu'il voit — et le nom réel
/// comme le CARNET D'ADRESSES vivent dans une autre base, servie par un autre
/// service.
///
/// Sans ce gestionnaire, un compte « supprimé » laisse derrière lui les adresses
/// de livraison de son titulaire, indéfiniment, là où plus rien ne signale
/// qu'elles auraient dû partir. C'est le genre de régression qu'aucune
/// compilation n'attrape : les deux services fonctionnent parfaitement, chacun
/// de son côté.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PurgeUserDataOnAccountAnonymizedHandler
    : IIntegrationEventHandler<UserAnonymizedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<PurgeUserDataOnAccountAnonymizedHandler> _logger;

    public PurgeUserDataOnAccountAnonymizedHandler(
        ISender sender, ILogger<PurgeUserDataOnAccountAnonymizedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserAnonymizedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new PurgeUserDataCommand(integrationEvent.UserId), cancellationToken);

        if (result.IsFailure)
        {
            // ON LÈVE, ET C'EST UNE CORRECTION D'UNE VERSION ANTÉRIEURE.
            //
            // Le code d'origine journalisait et retournait, avec un commentaire
            // affirmant « la commande est idempotente, l'outbox réessaiera ».
            // C'était faux : un gestionnaire qui retourne normalement est un
            // gestionnaire qui a RÉUSSI, du point de vue du dispatcher. Le
            // message était consommé et perdu.
            //
            // Une purge en échec ne repartait donc jamais, n'apparaissait dans
            // aucune lettre morte, ne déclenchait aucune alerte — il ne restait
            // qu'une ligne de journal que personne ne lit. Un commentaire
            // rassurant sur un mécanisme inexistant est pire que pas de
            // commentaire.
            //
            // En levant, le message est rejoué. C'est ce qu'on veut pour une
            // obligation légale non tenue.
            _logger.LogError(
                "Données personnelles NON purgées pour le compte supprimé {UserId} — {Code} : {Message}",
                integrationEvent.UserId, result.Error.Code, result.Error.Message);

            throw new InvalidOperationException(
                $"Purge des données personnelles impossible pour le compte {integrationEvent.UserId} : "
                + $"{result.Error.Code} — {result.Error.Message}");
        }
    }
}
