using System.Net;
// IHttpClientFactory vit dans System.Net.Http, apporté par
// Microsoft.Extensions.Http.
using System.Net.Http;
using System.Text;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Partners;
using HBA.Deliveries.Domain.Webhooks;
using HBA.Deliveries.Infrastructure.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Infrastructure.Webhooks;

/// <summary>LA BOUCLE QUI VIDE LA FILE DES WEBHOOKS.</summary>
internal sealed class WebhookDispatchService : BackgroundService
{
    /// <summary>Fréquence de balayage. La file est vide la plupart du temps.</summary>
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private const int BatchSize = 25;

    /// <summary>Étalement du recul, en secondes.</summary>
    private const int MaxJitterSeconds = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatchService> _logger;

    public WebhookDispatchService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatchService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Webhooks partenaires : boucle démarrée (balayage {Interval}s, {Max} tentatives max).",
            PollingInterval.TotalSeconds, WebhookDelivery.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Même verrou que le dispatch, sur une clé DIFFÉRENTE : deux
                // répliques enverraient sinon chaque webhook en double.
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider
                        .GetRequiredService<Persistence.DeliveriesDbContext>();

                    await using var runner = await SingleRunnerLock.TryAcquireAsync(
                        dbContext, SingleRunnerLock.WebhookKey, stoppingToken);

                    if (runner.Acquired)
                    {
                        await DrainAsync(stoppingToken);
                    }
                }

                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Une erreur de tour ne doit pas tuer la boucle : c'est le seul
                // chemin par lequel un partenaire apprend quoi que ce soit.
                _logger.LogError(ex, "Erreur pendant un tour d'envoi de webhooks.");

                try
                {
                    await Task.Delay(PollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var webhooks = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();
        var partners = scope.ServiceProvider.GetRequiredService<IPartnerRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDeliveryUnitOfWork>();

        var due = await webhooks.ListDueAsync(DateTime.UtcNow, BatchSize, cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);

        foreach (var webhook in due)
        {
            var partner = await partners.GetByIdAsync(new PartnerId(webhook.PartnerId), cancellationToken);

            // URL ET SECRET SONT RELUS MAINTENANT, PAS FIGÉS À LA MISE EN FILE.
            if (partner?.WebhookUrl is not { } url || partner.WebhookSecret is not { } secret)
            {
                // Pas de rappel configuré : on ABANDONNE tout de suite plutôt que
                // de réessayer six fois quelque chose qui ne peut pas aboutir.
                for (var i = webhook.Attempts; i < WebhookDelivery.MaxAttempts; i++)
                {
                    webhook.MarkFailed(null, "Aucun rappel configuré pour ce partenaire.");
                }

                continue;
            }

            await SendAsync(client, webhook, url, secret, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task SendAsync(
        HttpClient client, WebhookDelivery webhook, string url, string secret, CancellationToken cancellationToken)
    {
        var jitter = Random.Shared.Next(0, MaxJitterSeconds);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                // Le corps est celui FIGÉ à la mise en file, transmis tel quel :
                // c'est exactement cette chaîne qui est signée.
                Content = new StringContent(webhook.Payload, Encoding.UTF8, "application/json")
            };

            request.Headers.TryAddWithoutValidation(
                WebhookSignature.HeaderName, WebhookSignature.Build(webhook.Payload, secret, DateTime.UtcNow));

            request.Headers.TryAddWithoutValidation(
                WebhookSignature.EventIdHeaderName, webhook.EventId.ToString());

            request.Headers.TryAddWithoutValidation(
                WebhookSignature.EventTypeHeaderName, webhook.EventType);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            using var response = await client.SendAsync(request, timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                webhook.MarkDelivered((int)response.StatusCode);
                return;
            }

            webhook.MarkFailed((int)response.StatusCode, ReasonFor(response.StatusCode), jitter);

            _logger.LogWarning(
                "Webhook {EventType} refusé par le partenaire {PartnerId} : HTTP {Status} "
                + "(tentative {Attempt}/{Max}).",
                webhook.EventType, webhook.PartnerId, (int)response.StatusCode,
                webhook.Attempts, WebhookDelivery.MaxAttempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Arrêt de l'hôte : on ne compte PAS de tentative.
            throw;
        }
        catch (Exception ex)
        {
            // Délai dépassé, DNS injoignable, certificat invalide : le partenaire
            // n'a pas répondu.
            webhook.MarkFailed(null, ex.Message, jitter);

            _logger.LogWarning(
                "Webhook {EventType} non remis au partenaire {PartnerId} : {Error} "
                + "(tentative {Attempt}/{Max}).",
                webhook.EventType, webhook.PartnerId, ex.Message,
                webhook.Attempts, WebhookDelivery.MaxAttempts);
        }

        if (webhook.Status is WebhookStatus.Abandoned)
        {
            // Le seul message de niveau Error de ce fichier.
            _logger.LogError(
                "Webhook {EventType} ABANDONNÉ pour le partenaire {PartnerId} après {Attempts} tentatives "
                + "(événement {EventId}). Ce partenaire ne saura jamais que ce fait a eu lieu.",
                webhook.EventType, webhook.PartnerId, webhook.Attempts, webhook.EventId);
        }
    }

    /// <summary>Nom du client HTTP. Voir DeliveriesModuleInstaller pour sa configuration.</summary>
    public const string HttpClientName = "hba-delivery-webhooks";

    private static string ReasonFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "Refusé : vérifiez que le partenaire valide bien la signature avec le secret courant.",
        HttpStatusCode.NotFound =>
            "Endpoint introuvable : l'URL de rappel est peut-être erronée ou le déploiement en cours.",
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
            "Le partenaire n'a pas répondu à temps.",
        _ => $"HTTP {(int)status}."
    };
}
