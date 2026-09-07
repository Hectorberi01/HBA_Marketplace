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

/// <summary>Remet le code à usage unique à son destinataire, par SMS ou par e-mail.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SendOtpCodeHandler")]
public sealed class SendOtpCodeHandler : IIntegrationEventHandler<OtpChallengeIssuedIntegrationEvent>
{
    /// <summary>Les valeurs de `MfaChannels`, recopiées côté consommateur.</summary>
    private const string CanalSms = "SMS";
    private const string CanalEmail = "EMAIL";

    private readonly IEmailSender _email;
    private readonly ISmsSender _sms;
    private readonly ISecretProtector _protecteur;
    private readonly INotificationsUnitOfWork _unitOfWork;
    private readonly ILogger<SendOtpCodeHandler> _logger;

    public SendOtpCodeHandler(
        IEmailSender email,
        ISmsSender sms,
        ISecretProtector protecteur,
        INotificationsUnitOfWork unitOfWork,
        ILogger<SendOtpCodeHandler> logger)
    {
        _email = email;
        _sms = sms;
        _protecteur = protecteur;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        OtpChallengeIssuedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LE CODE ARRIVE CHIFFRÉ (ISSUE-071).
        var code = _protecteur.Unprotect(integrationEvent.ProtectedCode);

        // MINUTES CALCULÉES, PAS RECOPIÉES DE `MfaChallenge.Lifetime`.
        var restantes = Math.Max(1, (int)Math.Ceiling((integrationEvent.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes));

        // On NE capture PAS l'exception d'envoi.
        switch (integrationEvent.Channel)
        {
            case CanalSms:
                await _sms.SendAsync(
                    new SmsMessage(
                        integrationEvent.PhoneNumber,
                        AccountEmailTemplates.OneTimeCodeSms(code, restantes)),
                    cancellationToken);
                break;

            case CanalEmail:
                await _email.SendAsync(
                    AccountEmailTemplates.OneTimeCode(
                        integrationEvent.Email, integrationEvent.FirstName, code, restantes),
                    cancellationToken);
                break;

            default:
                // ON LÈVE, ON NE RETOMBE PAS SUR L'E-MAIL.
                throw new InvalidOperationException(
                    $"Canal OTP inconnu « {integrationEvent.Channel} » pour l'utilisateur "
                    + $"{integrationEvent.UserId}. Aucun envoi n'a été tenté : un repli sur un "
                    + "autre canal remettrait le code ailleurs que là où il a été demandé.");
        }

        // On journalise l'utilisateur et le canal, JAMAIS le code ni les
        // coordonnées.
        _logger.LogInformation(
            "Code à usage unique remis à l'utilisateur {UserId} par {Channel}.",
            integrationEvent.UserId, integrationEvent.Channel);

        // CE `SaveChanges` NE SAUVEGARDE RIEN A NOUS — IL COMMITTE LA TRACE.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

    }
}
