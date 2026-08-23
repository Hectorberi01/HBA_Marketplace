using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Communication.Notifications.Application.Emails;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

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
public sealed class SendEmailVerificationHandler : IIntegrationEventHandler<EmailVerificationRequestedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly ISecretProtector _protecteur;
    private readonly ILogger<SendEmailVerificationHandler> _logger;

    public SendEmailVerificationHandler(
        IEmailSender email,
        ISecretProtector protecteur,
        ILogger<SendEmailVerificationHandler> logger)
    {
        _email = email;
        _protecteur = protecteur;
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
public sealed class SendPasswordResetEmailHandler : IIntegrationEventHandler<PasswordResetRequestedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly ISecretProtector _protecteur;
    private readonly ILogger<SendPasswordResetEmailHandler> _logger;

    public SendPasswordResetEmailHandler(
        IEmailSender email,
        ISecretProtector protecteur,
        ILogger<SendPasswordResetEmailHandler> logger)
    {
        _email = email;
        _protecteur = protecteur;
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
    }
}
