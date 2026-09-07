using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Events;
using System.Reflection;
using HBA.Shared.IntegrationEvents;

namespace HBA.Shared.Infrastructure.Events;

/// <summary>
/// Dispatch in-process d'un event d'intégration vers tous ses handlers enregistrés.
/// </summary>
public sealed class IntegrationEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntegrationEventDispatcher>? _logger;

    public IntegrationEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<IntegrationEventDispatcher>>();
    }

    public async Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
        var handlers = _serviceProvider.GetServices(handlerType).Where(h => h is not null).ToList();

        if (handlers.Count == 0)
        {
            return;
        }

        // `GetServices` ET NON `GetService` — voir l'encadré de cette classe.
        var inboxes = _serviceProvider.GetServices<IConsumerInbox>()
            .Where(i => i is not null)
            .ToList();

        if (inboxes.Count == 0)
        {
            _logger?.LogWarning(
                "Aucun IConsumerInbox enregistré : {Nombre} handler(s) de {Evenement} s'exécutent sans garde "
                + "d'idempotence. Kafka livre au moins une fois — un rejeu refera leur effet.",
                handlers.Count, integrationEvent.GetType().Name);
        }

        var typeEvenement = integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name;
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;

        foreach (var handler in handlers)
        {
            var consommateur = NomDuConsommateur(handler!);

            if (inboxes.Count > 0)
            {
                // UNE SEULE INBOX QUI CONNAÎT L'ÉVÉNEMENT SUFFIT À LE SAUTER.
                var dejaTraite = false;

                foreach (var inbox in inboxes)
                {
                    if (await inbox.HasProcessedAsync(integrationEvent.Id, consommateur, cancellationToken))
                    {
                        dejaTraite = true;
                        break;
                    }
                }

                if (dejaTraite)
                {
                    _logger?.LogDebug(
                        "Événement {EventId} déjà traité par {Consommateur} : ignoré.",
                        integrationEvent.Id, consommateur);
                    continue;
                }

                // Ajout aux contextes, sans écriture : la ligne partira avec le
                // `SaveChangesAsync` du handler.
                foreach (var inbox in inboxes)
                {
                    await inbox.MarkProcessedAsync(
                        integrationEvent.Id,
                        consommateur,
                        typeEvenement,
                        // VALAIT `null`, ALORS QUE L'INFORMATION ÉTAIT LÀ.
                        correlationId: string.IsNullOrWhiteSpace(HbaRequestContext.Current.CorrelationId)
                            ? null
                            : HbaRequestContext.Current.CorrelationId,
                        cancellationToken);
                }
            }

            await (Task)method.Invoke(handler, new object[] { integrationEvent, cancellationToken })!;
        }
    }

    /// <summary>Le nom sous lequel ce handler est inscrit dans `consumer_inbox`.</summary>
    private static string NomDuConsommateur(object handler)
    {
        var type = handler.GetType();

        // L'ATTRIBUT L'EMPORTE SUR LE NOM DU TYPE, ET C'EST TOUT L'INTERET.
        var nom = type.GetCustomAttribute<NomDeConsommateurAttribute>(inherit: false)?.Nom
                  ?? type.FullName
                  ?? type.Name;

        // La colonne fait 120 caractères (ConsumerInboxConfiguration).
        return nom.Length <= 120 ? nom : nom[^120..];
    }
}
