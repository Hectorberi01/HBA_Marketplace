using Microsoft.Extensions.Logging;
using HBA.Communication.Notifications.Application.Abstractions;

namespace HBA.Communication.Notifications.Infrastructure.Sms;

/// <summary>
/// Adaptateur de DÉVELOPPEMENT : n'envoie rien, écrit le SMS dans la console.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CET ADAPTATEUR ÉCRIT DES CODES OTP EN CLAIR DANS LES LOGS. C'EST ASSUMÉ — ET
///     STRICTEMENT RÉSERVÉ AU DÉVELOPPEMENT.
///
/// Même raisonnement que <c>DevelopmentEmailSender</c>, et la même leçon : sans lui,
/// un développeur local ne pourrait jamais franchir l'étape OTP, et quelqu'un finirait
/// par « résoudre » le problème en renvoyant le code dans la réponse HTTP. C'est
/// littéralement ce qui est arrivé au jeton de réinitialisation de mot de passe sur
/// cette plateforme.
///
/// La différence tient à la GARDE : `NotificationsModuleInstaller` REFUSE DE DÉMARRER
/// en Production sans fournisseur SMS configuré. Cet adaptateur ne peut donc pas s'y
/// retrouver par distraction.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
