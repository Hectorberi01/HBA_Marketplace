using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Application.Offers;
using HBA.Merchants.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Catalog.Infrastructure;

using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
// FERMER UNE BOUTIQUE NE RETIRAIT RIEN DE LA VENTE (ISSUE-041).

namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// La boutique ferme — décision du vendeur ou de la plateforme, indistinctement :
/// ses offres quittent la vente.
/// </summary>
public sealed class StoreClosedOfferWithdrawalHandler
    : IIntegrationEventHandler<StoreClosedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
    private const string ConsumerName = "catalog-service.merchants-store-closed";

    private readonly ISender _sender;
    private readonly IConsumerInbox _inbox;
    private readonly ILogger<StoreClosedOfferWithdrawalHandler> _logger;

    public StoreClosedOfferWithdrawalHandler(
        ISender sender,
        IConsumerInbox inbox,
        ILogger<StoreClosedOfferWithdrawalHandler> logger)
    {
        _sender = sender;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task HandleAsync(
        StoreClosedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.store.closed",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        var resultat = await _sender.Send(
            new SuspendStoreCatalogCommand(e.StoreId, e.Reason), cancellationToken);

        if (resultat.IsFailure)
        {
            // ON LÈVE PLUTÔT QUE DE JOURNALISER ET DE CONTINUER.
            throw new InvalidOperationException(
                $"Fermeture de la boutique {e.StoreId} non appliquée au catalogue : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Boutique {StoreId} fermée : offres retirées de la vente.", e.StoreId);
    }
}

/// <summary>La plateforme suspend la boutique.</summary>
public sealed class StoreSuspendedOfferWithdrawalHandler
    : IIntegrationEventHandler<StoreSuspendedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.merchants-store-suspended";

    private readonly ISender _sender;
    private readonly IConsumerInbox _inbox;
    private readonly ILogger<StoreSuspendedOfferWithdrawalHandler> _logger;

    public StoreSuspendedOfferWithdrawalHandler(
        ISender sender,
        IConsumerInbox inbox,
        ILogger<StoreSuspendedOfferWithdrawalHandler> logger)
    {
        _sender = sender;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task HandleAsync(
        StoreSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // UN NOM DE CONSUMER PAR HANDLER, JAMAIS UN PAR FICHIER.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.store.suspended",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        var resultat = await _sender.Send(
            new SuspendStoreCatalogCommand(e.StoreId, e.Reason), cancellationToken);

        if (resultat.IsFailure)
        {
            throw new InvalidOperationException(
                $"Suspension de la boutique {e.StoreId} non appliquée au catalogue : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Boutique {StoreId} suspendue par la plateforme : offres retirées de la vente.",
            e.StoreId);
    }
}

/// <summary>
/// La boutique rouvre : les offres retirées PAR CETTE FERMETURE reviennent, et rien
/// d'autre.
/// </summary>
public sealed class StoreOpenedOfferReinstatementHandler
    : IIntegrationEventHandler<StoreOpenedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.merchants-store-opened";

    private readonly ISender _sender;
    private readonly IConsumerInbox _inbox;
    private readonly ILogger<StoreOpenedOfferReinstatementHandler> _logger;

    public StoreOpenedOfferReinstatementHandler(
        ISender sender,
        IConsumerInbox inbox,
        ILogger<StoreOpenedOfferReinstatementHandler> logger)
    {
        _sender = sender;
        _inbox = inbox;
        _logger = logger;
    }

    public async Task HandleAsync(
        StoreOpenedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.store.opened",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        var resultat = await _sender.Send(
            new ReinstateStoreCatalogCommand(e.StoreId), cancellationToken);

        if (resultat.IsFailure)
        {
            throw new InvalidOperationException(
                $"Réouverture de la boutique {e.StoreId} non appliquée au catalogue : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        // CE QUE LA COMMANDE REND N'EST PAS DÉCORATIF, ET N'EST PAS EXPLOITÉ ICI.
        _logger.LogInformation(
            "Boutique {StoreId} rouverte : {Offres} offre(s) remise(s) en vente.",
            e.StoreId, resultat.Value.Count);
    }
}
