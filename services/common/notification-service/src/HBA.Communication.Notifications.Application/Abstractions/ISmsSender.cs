namespace HBA.Communication.Notifications.Application.Abstractions;

/// <summary>Un SMS transactionnel : numéro international, texte brut.</summary>
/// <param name="To">Numéro au format international.</param>
/// <param name="Text">Corps du message. Pas de HTML, pas de lien long.</param>
public sealed record SmsMessage(string To, string Text);

/// <summary>Port d'envoi de SMS transactionnels.</summary>
public interface ISmsSender
{
    /// <summary>Envoie le SMS. Lève en cas d'échec.</summary>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
