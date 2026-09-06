using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Communication.Notifications.Application.Emails;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
//
// Il vivait dans `HBA.Communication.Notifications.Application.Notifications.EventHandlers` et y resolvait ses voisins SANS `using` : le
// compilateur cherche d'abord dans les espaces de noms englobants. Descendu
// dans `Messaging/Kafka/Consumers`, il a perdu ce voisinage — d'ou les lignes
// ci-dessous, qui rendent explicite ce qui etait implicite.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Envoie l'e-mail de vérification d'adresse à l'inscription.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER N'EXISTAIT PAS. AUCUN COMPTE N'A JAMAIS REÇU SON LIEN DE VÉRIFICATION.
///
/// `EmailVerificationRequestedIntegrationEvent` était publié consciencieusement à chaque
/// inscription, avec le jeton, depuis le premier jour. Et il n'avait AUCUN consommateur.
/// L'événement partait dans l'outbox, était marqué traité, et disparaissait.
///
/// MediatR et le dispatcher d'événements d'intégration résolvent LAZILY : un événement
/// sans handler ne provoque aucune erreur, aucun avertissement, rien. Il est simplement
/// ignoré — en silence. C'est le mode de défaillance le plus coûteux de cette
/// architecture : le code a l'air complet, et il ne fait rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
//
// `IntegrationEventDispatcher` la derivait du nom complet du type. Descendre ce
// fichier dans `Messaging/Kafka/Consumers` a change son espace de noms, donc sa
// cle, donc a orpheline ses traces dans `consumer_inbox` : au premier rejeu,
// chaque evenement deja traite serait repasse pour neuf.
//
// Les valeurs ci-dessous reproduisent le nom complet d'AVANT le deplacement.
// Ce sont des cles de base de donnees : elles ne se refactorisent pas.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SendEmailVerificationHandler")]
public sealed class SendEmailVerificationHandler : IIntegrationEventHandler<EmailVerificationRequestedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly ISecretProtector _protecteur;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly ILogger<SendEmailVerificationHandler> _logger;

    public SendEmailVerificationHandler(
        IEmailSender email,
        ISecretProtector protecteur,
        INotificationsUnitOfWork unitOfWork,
        ILogger<SendEmailVerificationHandler> logger)
    {
        _email = email;
        _protecteur = protecteur;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        EmailVerificationRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LE CODE ARRIVE CHIFFRÉ. Il a traversé l'outbox d'identity puis Kafka, où il
        // ne devait plus être lisible — c'était le défaut ISSUE-071. On le déchiffre ici,
        // au dernier moment, juste avant de le remettre à son destinataire légitime.
        //
        // On NE capture PAS l'échec de déchiffrement : une charge illisible signifie que
        // les deux services n'ont pas la même `Security:SecretProtection:Key`. Envoyer un
        // e-mail avec un code de remplacement serait pire que ne rien envoyer — l'erreur
        // remonte, le message repasse en lettre morte, et la panne se voit.
        var code = _protecteur.Unprotect(integrationEvent.ProtectedVerificationToken);

        var message = AccountEmailTemplates.EmailVerificationCode(
            integrationEvent.Email, integrationEvent.FirstName, code);

        // On NE capture PAS l'exception. Un échec laisse le message d'outbox non traité,
        // donc rejoué au tour suivant. Avaler l'erreur perdrait définitivement l'e-mail —
        // et l'utilisateur resterait bloqué à la porte, sans que personne ne le sache.
        await _email.SendAsync(message, cancellationToken);

        // On journalise l'utilisateur, JAMAIS l'URL : elle contient le jeton.
        _logger.LogInformation(
            "E-mail de vérification envoyé à l'utilisateur {UserId}.", integrationEvent.UserId);

        // ═════════════════════════════════════════════════════════════════════
        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        //
        // `IntegrationEventDispatcher` ajoute l'entree d'inbox au contexte AVANT
        // d'appeler ce gestionnaire, et ne la sauvegarde pas : elle doit partir
        // avec la transaction de l'effet metier. Ce gestionnaire n'ecrivait rien
        // en base — il envoie, c'est tout — donc la trace restait en attente et
        // n'etait JAMAIS committee.
        //
        // Consequence : l'evenement restait « jamais traite » pour l'inbox. Au
        // premier rejeu — remise a zero d'offsets, rebalancement de partition —
        // l'e-mail de verification repartait, avec un lien deja consomme.
        //
        // L'APPEL EST APRES L'ENVOI, ET C'EST DELIBERE. Sauvegarder avant
        // marquerait l'evenement traite pour un envoi qui peut encore echouer :
        // le message ne partirait jamais et rien ne le rejouerait. Dans l'autre
        // sens, un echec de sauvegarde apres un envoi reussi fait un doublon —
        // Kafka livre au moins une fois, c'est le cote acceptable de l'arbitrage.
        // ═════════════════════════════════════════════════════════════════════
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}

/// <summary>
/// Envoie l'e-mail de réinitialisation de mot de passe.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST L'EXISTENCE DE CE HANDLER QUI REND LA PLATEFORME SÛRE.
///
/// Faute de canal e-mail, le jeton de réinitialisation n'avait nulle part où aller. Il a
/// donc été renvoyé dans la RÉPONSE HTTP d'un endpoint ANONYME
/// (`POST /mobile/auth/password/forgot`) — avec un « TODO production » en guise
/// d'excuse. N'importe qui saisissait l'e-mail d'un administrateur, lisait son jeton, et
/// prenait son compte.
///
/// Le jeton a maintenant un chemin légitime : Identity → outbox → ici → boîte mail du
/// propriétaire. Et de personne d'autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SendPasswordResetEmailHandler")]
public sealed class SendPasswordResetEmailHandler : IIntegrationEventHandler<PasswordResetRequestedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly ISecretProtector _protecteur;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly ILogger<SendPasswordResetEmailHandler> _logger;

    public SendPasswordResetEmailHandler(
        IEmailSender email,
        ISecretProtector protecteur,
        INotificationsUnitOfWork unitOfWork,
        ILogger<SendPasswordResetEmailHandler> logger)
    {
        _email = email;
        _protecteur = protecteur;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        PasswordResetRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // MÊME CHOSE QUE POUR LA VÉRIFICATION : le code arrive chiffré et n'est rendu
        // lisible qu'ici. La variable s'appelle `code` et rien d'autre — surtout pas un nom
        // qui finirait dans un message de journal par mégarde.
        var code = _protecteur.Unprotect(integrationEvent.ProtectedResetToken);

        var message = AccountEmailTemplates.PasswordResetCode(
            integrationEvent.Email, integrationEvent.FirstName, code);

        await _email.SendAsync(message, cancellationToken);

        // NI le jeton, NI l'URL (qui le contient), NI l'e-mail ne sont journalisés ici.
        // Un jeton dans les logs est un jeton lisible par quiconque a accès aux logs — et
        // ce serait recréer, en plus discret, la fuite qu'on vient de fermer.
        _logger.LogInformation(
            "E-mail de réinitialisation envoyé à l'utilisateur {UserId}.", integrationEvent.UserId);

        // ═════════════════════════════════════════════════════════════════════
        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        //
        // `IntegrationEventDispatcher` ajoute l'entree d'inbox au contexte AVANT
        // d'appeler ce gestionnaire, et ne la sauvegarde pas : elle doit partir
        // avec la transaction de l'effet metier. Ce gestionnaire n'ecrivait rien
        // en base — il envoie, c'est tout — donc la trace restait en attente et
        // n'etait JAMAIS committee.
        //
        // Consequence : l'evenement restait « jamais traite » pour l'inbox. Au
        // premier rejeu — remise a zero d'offsets, rebalancement de partition —
        // le code de reinitialisation repartait — un justificatif de prise de
        // compte, renvoye sans que personne ne l'ait demande.
        //
        // L'APPEL EST APRES L'ENVOI, ET C'EST DELIBERE. Sauvegarder avant
        // marquerait l'evenement traite pour un envoi qui peut encore echouer :
        // le message ne partirait jamais et rien ne le rejouerait. Dans l'autre
        // sens, un echec de sauvegarde apres un envoi reussi fait un doublon —
        // Kafka livre au moins une fois, c'est le cote acceptable de l'arbitrage.
        // ═════════════════════════════════════════════════════════════════════
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}
