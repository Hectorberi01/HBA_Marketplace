using HBA.Shared.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Orders.Infrastructure.Persistence.Inbox;
using HBA.Orders.Infrastructure.Persistence.Outbox;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Observability;
using HBA.Shared.Infrastructure.Events;
using HBA.Shared.Infrastructure.Kafka;
using HBA.Shared.Infrastructure.Serialization;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using HBA.Shared.Infrastructure.Observability;

using HBA.Orders.Infrastructure.Messaging.Kafka.Retry;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Processors;

/// <summary>
/// Processeur d'outbox d'un module : lit les messages éligibles, les publie
/// (dispatch in-process) et les marque traités.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OutboxRetryPolicy _retryPolicy;
    private readonly IOutboxMetrics _metrics;
    private readonly Random _random = new();

    /// <summary>
    /// Nom du module, déduit du DbContext : « WalletDbContext » → « Settlement ».
    /// </summary>
    private static readonly string ModuleName = typeof(OrderingDbContext).Name.Replace("DbContext", string.Empty);
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor> logger,
        OutboxRetryPolicy retryPolicy,
        IOutboxMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retryPolicy = retryPolicy;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Arrêt normal de l'hôte : on sort proprement sans faire planter le
                // host.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement de l'outbox de {Context}", typeof(OrderingDbContext).Name);

                // Pause avant nouvel essai, elle aussi protégée contre
                // l'annulation.
                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IKafkaIntegrationEventPublisher>();

        var nowUtc = DateTime.UtcNow;

        // Sont ÉLIGIBLES les messages non traités, non enterrés, et dont la
        // temporisation est écoulée.
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null
                        && m.DeadLetteredOnUtc == null
                        && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= nowUtc))
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            // ON REJOUE LE CONTEXTE DE TRACE DE LA REQUÊTE D'ORIGINE.
            var parent = ActivityContext.TryParse(message.TraceParent, traceState: null, out var contexte)
                ? contexte
                : default;

            using var activite = HbaTelemetry.Kafka.StartActivity(
                "outbox publish", ActivityKind.Producer, parent);

            activite?.SetTag("messaging.system", "kafka");
            activite?.SetTag("messaging.operation", "publish");
            activite?.SetTag("hba.outbox.message_id", message.Id);
            activite?.SetTag("hba.outbox.event_type", message.Type);
            activite?.SetTag("hba.outbox.attempt", message.AttemptCount + 1);

            // ON RÉTABLIT LA CORRÉLATION MÉTIER, PAS SEULEMENT LA TRACE.
            using var correlation = HbaRequestContext.BeginScope(new HbaRequestContext
            {
                CorrelationId = message.CorrelationId ?? string.Empty,
                CausationId = message.Id.ToString()
            });

            try
            {
                var type = EventTypeName.Resolve(message.Type);
                if (JsonSerializer.Deserialize(message.Content, type, SerializerOptions) is not IntegrationEvent integrationEvent)
                {
                    throw new InvalidOperationException($"Désérialisation impossible pour {message.Type}");
                }

                await publisher.PublishAsync(integrationEvent, cancellationToken);

                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = null;
                message.NextAttemptAtUtc = null;
            }
            catch (Exception ex)
            {
                activite?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activite?.AddException(ex);

                HandleFailure(message, ex);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void HandleFailure(OutboxMessage message, Exception exception)
    {
        // La DÉCISION (temporiser ou enterrer) appartient à la politique —
        // testable, et testée.
        var deadLettered = _retryPolicy.RegisterFailure(message, exception.Message, _random, DateTime.UtcNow);

        if (deadLettered)
        {
            // CRITICAL, et pas Error. Ce n'est plus « une tentative a échoué » —
            // c'est « cet événement métier ne sera JAMAIS traité ».
            _logger.LogCritical(
                exception,
                "LETTRE MORTE — outbox {Context}, message {MessageId} de type {Type} abandonné après {Attempts} tentatives. "
                + "CET ÉVÉNEMENT NE SERA JAMAIS TRAITÉ, et aucune route de rejeu n'existe. Corriger la cause, puis "
                + "remettre la ligne en file À LA MAIN : DeadLetteredOnUtc = NULL, AttemptCount = 0, "
                + "NextAttemptAtUtc = NULL sur cette ligne de la table outbox_messages du service.",
                typeof(OrderingDbContext).Name, message.Id, message.Type, message.AttemptCount);

            // LA métrique qui doit rester à zéro.
            _metrics.DeadLettered(ModuleName, message.Type);

            return;
        }

        // Warning, pas Error : un échec isolé est ATTENDU (le réseau tombe, un
        // fournisseur hoquette, la base redémarre).
        _metrics.PublishFailed(ModuleName, message.Type);

        _logger.LogWarning(
            exception,
            "Échec de publication du message {MessageId} ({Type}), tentative {Attempts}/{Max}. Prochain essai à {NextAttempt:u}.",
            message.Id, message.Type, message.AttemptCount, _retryPolicy.MaxAttempts, message.NextAttemptAtUtc);
    }
}
