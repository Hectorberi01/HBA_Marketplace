using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Products;
using HBA.Merchants.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Catalog.Infrastructure;

using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
// CE FICHIER A DÉMÉNAGÉ DE `Application` VERS
// `Infrastructure/Messaging/Kafka/Consumers`.

namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;

// CES HANDLERS NE SONT PLUS LES SEULS, ET NE SONT PLUS LES PRINCIPAUX.
/// <summary>
/// Fermeture d'un compte vendeur (suppression partielle) : on RETIRE ses produits
/// de la vente.
/// </summary>
public sealed class SellerClosedProductInvalidationHandler : IIntegrationEventHandler<SellerClosedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
    private const string ConsumerName = "catalog-service.merchants-seller-closed";

    private readonly IProductRepository _products;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerClosedProductInvalidationHandler> _logger;

    public SellerClosedProductInvalidationHandler(
        IProductRepository products,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerClosedProductInvalidationHandler> logger)
    {
        _products = products;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerClosedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // GARDE D'IDEMPOTENCE DU §19.5 — ET CE QU'ELLE APPORTE VRAIMENT ICI.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var products = await _products.ListBySellerForUpdateAsync(e.SellerId, cancellationToken);

        var unpublished = 0;
        foreach (var product in products)
        {
            // « Active » S'APPELLE MAINTENANT « Published », ET LA GARDE COMPTE.
            if (product.Status == ProductStatus.Published)
            {
                product.Unpublish();
                unpublished++;
            }
        }

        // LA TRACE EST ÉCRITE DANS LA MÊME UNITÉ DE TRAVAIL QUE L'EFFET.
        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.seller.closed",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vendeur {SellerId} fermé : {Count} produit(s) dépublié(s).", e.SellerId, unpublished);
    }
}

/// <summary>
/// Suppression DÉFINITIVE d'un vendeur (admin) : on ARCHIVE tous ses produits
/// (transition terminale) pour les retirer irrévocablement de la vente.
/// </summary>
public sealed class SellerDeletedProductPurgeHandler : IIntegrationEventHandler<SellerDeletedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.merchants-seller-deleted";

    private readonly IProductRepository _products;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerDeletedProductPurgeHandler> _logger;

    public SellerDeletedProductPurgeHandler(
        IProductRepository products,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerDeletedProductPurgeHandler> logger)
    {
        _products = products;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerDeletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // DEUX NOMS DE CONSUMER DISTINCTS, PAS UN SEUL POUR LE FICHIER.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var products = await _products.ListBySellerForUpdateAsync(e.SellerId, cancellationToken);

        var archived = 0;
        foreach (var product in products)
        {
            if (product.Status != ProductStatus.Archived)
            {
                product.Archive();
                archived++;
            }
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.seller.deleted",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vendeur {SellerId} supprimé : {Count} produit(s) archivé(s).", e.SellerId, archived);
    }
}
