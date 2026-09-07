using Microsoft.Extensions.Logging;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Shared.Application.Abstractions;
using HBA.Communication.Notifications.Application.Emails;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Envoie l'e-mail de vérification d'adresse à l'inscription.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
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
        // LE CODE ARRIVE CHIFFRÉ. Il a traversé l'outbox d'identity puis Kafka, où
        // il ne devait plus être lisible — c'était le défaut ISSUE-071.
        var code = _protecteur.Unprotect(integrationEvent.ProtectedVerificationToken);

        var message = AccountEmailTemplates.EmailVerificationCode(
            integrationEvent.Email, integrationEvent.FirstName, code);

        // On NE capture PAS l'exception.
        await _email.SendAsync(message, cancellationToken);

        // On journalise l'utilisateur, JAMAIS l'URL : elle contient le jeton.
        _logger.LogInformation(
            "E-mail de vérification envoyé à l'utilisateur {UserId}.", integrationEvent.UserId);

        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}

/// <summary>Envoie l'e-mail de réinitialisation de mot de passe.</summary>
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
        // MÊME CHOSE QUE POUR LA VÉRIFICATION : le code arrive chiffré et n'est
        // rendu lisible qu'ici.
        var code = _protecteur.Unprotect(integrationEvent.ProtectedResetToken);

        var message = AccountEmailTemplates.PasswordResetCode(
            integrationEvent.Email, integrationEvent.FirstName, code);

        await _email.SendAsync(message, cancellationToken);

        // NI le jeton, NI l'URL (qui le contient), NI l'e-mail ne sont journalisés
        // ici.
        _logger.LogInformation(
            "E-mail de réinitialisation envoyé à l'utilisateur {UserId}.", integrationEvent.UserId);

        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}
