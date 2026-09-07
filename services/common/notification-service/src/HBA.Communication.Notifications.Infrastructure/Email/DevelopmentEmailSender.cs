using Microsoft.Extensions.Logging;
using HBA.Communication.Notifications.Application.Abstractions;

namespace HBA.Communication.Notifications.Infrastructure.Email;

/// <summary>Adaptateur de DÉVELOPPEMENT : n'envoie rien, écrit l'e-mail dans la console.</summary>
public sealed class DevelopmentEmailSender : IEmailSender
{
    private readonly ILogger<DevelopmentEmailSender> _logger;

    public DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[E-MAIL NON ENVOYÉ — MODE DÉVELOPPEMENT]\n" +
            "  À      : {To}\n" +
            "  Sujet  : {Subject}\n" +
            "  Texte  :\n{TextBody}\n" +
            "  (Configurer Notifications:Email pour un envoi réel.)",
            message.To, message.Subject, message.TextBody);

        return Task.CompletedTask;
    }
}
