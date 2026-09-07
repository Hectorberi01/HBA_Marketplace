using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using HBA.Communication.Notifications.Application.Abstractions;

namespace HBA.Communication.Notifications.Infrastructure.Email;

/// <summary>Envoi d'e-mails via l'API HTTP de Resend.</summary>
public sealed class ResendEmailSender : IEmailSender
{
    public const string HttpClientName = "resend";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        IHttpClientFactory httpClientFactory,
        EmailOptions options,
        ILogger<ResendEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await client.PostAsJsonAsync(
            "https://api.resend.com/emails",
            new
            {
                from = _options.From,
                to = new[] { message.To },
                subject = message.Subject,
                html = message.HtmlBody,
                text = message.TextBody,
            },
            cancellationToken);

        // PostAsJsonAsync NE LÈVE PAS sur 4xx/5xx.
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // On journalise le DESTINATAIRE et le sujet, jamais le corps : il
            // contient le jeton de réinitialisation en clair.
            _logger.LogError(
                "Échec d'envoi d'e-mail à {To} (sujet « {Subject} ») : Resend a répondu {Status}. Réponse : {Body}",
                message.To, message.Subject, (int)response.StatusCode, body);

            // On lève : l'OutboxProcessor laissera le message non traité et le
            // rejouera.
            throw new InvalidOperationException(
                $"Resend a refusé l'envoi ({(int)response.StatusCode}). L'e-mail sera rejoué par l'outbox.");
        }

        _logger.LogInformation("E-mail « {Subject} » envoyé à {To}.", message.Subject, message.To);
    }
}
