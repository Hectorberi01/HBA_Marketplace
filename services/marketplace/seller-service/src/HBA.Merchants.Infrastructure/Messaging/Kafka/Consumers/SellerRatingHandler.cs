using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Merchants.Infrastructure;

using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
// CE FICHIER VIT DANS `Infrastructure/Messaging/Kafka/Consumers`, ET NON DANS
// `Application`.

namespace HBA.Merchants.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LA NOTE DU VENDEUR, ENFIN ALIMENTÉE.</summary>
public sealed class SellerRatingHandler
    : IIntegrationEventHandler<SellerRatingRecomputedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
    private const string ConsumerName = "seller-service.engagement-seller-rating-recomputed";

    private readonly ISellerRepository _sellers;
    private readonly IConsumerInbox _inbox;
    private readonly ISellerUnitOfWork _unitOfWork;
    private readonly ILogger<SellerRatingHandler> _logger;

    public SellerRatingHandler(
        ISellerRepository sellers,
        IConsumerInbox inbox,
        ISellerUnitOfWork unitOfWork,
        ILogger<SellerRatingHandler> logger)
    {
        _sellers = sellers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerRatingRecomputedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            return;
        }

        var seller = await _sellers.GetByIdAsync(new SellerId(e.SellerId), cancellationToken);

        if (seller is null)
        {
            // ON TRACE QUAND MÊME, ET ON NE LÈVE PAS.
            await MarquerTraiteAsync(e, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // `double` → `decimal`, ET LA BORNE EST DÉJÀ DANS L'AGRÉGAT.
        var resultat = seller.UpdateRating((decimal)e.Average);

        if (resultat.IsFailure)
        {
            _logger.LogWarning(
                "Note {Moyenne} refusée pour le vendeur {SellerId} — {Code}. La note affichée "
                + "reste l'ancienne.",
                e.Average, e.SellerId, resultat.Error.Code);
        }

        await MarquerTraiteAsync(e, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task MarquerTraiteAsync(
        SellerRatingRecomputedIntegrationEvent e, CancellationToken cancellationToken)
        => _inbox.MarkProcessedAsync(
            e.Id,
            ConsumerName,
            "seller.rating.recomputed",
            HbaRequestContext.Current.CorrelationId,
            cancellationToken);
}
