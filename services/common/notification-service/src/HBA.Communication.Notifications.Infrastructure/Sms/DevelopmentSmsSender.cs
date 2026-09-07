using Microsoft.Extensions.Logging;
using HBA.Communication.Notifications.Application.Abstractions;

namespace HBA.Communication.Notifications.Infrastructure.Sms;

/// <summary>Adaptateur de DÉVELOPPEMENT : n'envoie rien, écrit le SMS dans la console.</summary>
public sealed class DevelopmentSmsSender : ISmsSender
{
    private readonly ILogger<DevelopmentSmsSender> _logger;

    public DevelopmentSmsSender(ILogger<DevelopmentSmsSender> logger) => _logger = logger;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[SMS NON ENVOYÉ — MODE DÉVELOPPEMENT]\n" +
            "  À     : {To}\n" +
            "  Texte : {Text}\n" +
            "  (Configurer Notifications:Sms pour un envoi réel.)",
            message.To, message.Text);

        return Task.CompletedTask;
    }
}
