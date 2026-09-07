namespace HBA.Communication.Notifications.Application.Abstractions;

/// <summary>
/// Un e-mail transactionnel : destinataire, sujet, corps HTML, et sa version texte.
/// </summary>
/// <param name="To">Adresse du destinataire.</param>
/// <param name="Subject">Sujet.</param>
/// <param name="HtmlBody">Corps HTML.</param>
/// <param name="TextBody">Corps texte brut. Obligatoire, pas facultatif.</param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Port d'envoi d'e-mails transactionnels.</summary>
public interface IEmailSender
{
    /// <summary>Envoie l'e-mail. Lève en cas d'échec (réseau, 4xx/5xx du fournisseur).</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
