using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Catalog.Infrastructure;

using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LE STOCK DÉCIDE DE LA MISE EN VENTE — ET PERSONNE NE L'ÉCOUTAIT (ISSUE-047).</summary>
public sealed class WithdrawOffersOnStockDepletedHandler
    : IIntegrationEventHandler<StockDepletedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.inventory-stock-depleted";

    private readonly IProductOfferRepository _offers;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<WithdrawOffersOnStockDepletedHandler> _logger;

    public WithdrawOffersOnStockDepletedHandler(
        IProductOfferRepository offers,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<WithdrawOffersOnStockDepletedHandler> logger)
    {
        _offers = offers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        StockDepletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var offres = await _offers.ListBySkuAsync(e.Sku, cancellationToken);

        // SEULES LES OFFRES `Active` SONT RETIRÉES, ET SEULES CELLES DU LIEU
        // CONCERNÉ.
        var concernees = offres
            .Where(o => o.Status == OfferStatus.Active && o.ShipFromLocationId == e.LocationId)
            .ToList();

        var retirees = 0;

        foreach (var offre in concernees)
        {
            if (offre.MarkOutOfStock().IsSuccess)
            {
                retirees++;
            }
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "inventory.stock.depleted",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (retirees > 0)
        {
            _logger.LogInformation(
                "Rupture sur {Sku} au lieu {LocationId} : {Offres} offre(s) retirée(s) de la vente.",
                e.Sku, e.LocationId, retirees);
        }
    }
}

/// <summary>
/// Le stock remonte : les offres que LA RUPTURE avait retirées reviennent, et rien
/// d'autre.
/// </summary>
public sealed class ReactivateOffersOnStockReplenishedHandler
    : IIntegrationEventHandler<StockReplenishedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.inventory-stock-replenished";

    private readonly IProductOfferRepository _offers;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<ReactivateOffersOnStockReplenishedHandler> _logger;

    public ReactivateOffersOnStockReplenishedHandler(
        IProductOfferRepository offers,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<ReactivateOffersOnStockReplenishedHandler> logger)
    {
        _offers = offers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        StockReplenishedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var offres = await _offers.ListBySkuAsync(e.Sku, cancellationToken);

        var concernees = offres
            .Where(o => o.Status == OfferStatus.OutOfStock && o.ShipFromLocationId == e.LocationId)
            .ToList();

        var remises = 0;

        foreach (var offre in concernees)
        {
            if (offre.Activate().IsSuccess)
            {
                remises++;
            }
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "inventory.stock.replenished",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (remises > 0)
        {
            _logger.LogInformation(
                "Réassort sur {Sku} au lieu {LocationId} : {Offres} offre(s) remise(s) en vente.",
                e.Sku, e.LocationId, remises);
        }
    }
}
