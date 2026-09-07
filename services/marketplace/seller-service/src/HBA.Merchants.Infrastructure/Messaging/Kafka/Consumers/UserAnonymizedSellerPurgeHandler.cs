using HBA.Identity.Contracts.IntegrationEvents;
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

/// <summary>
/// UN COMPTE EST ANONYMISÉ → SON DOSSIER VENDEUR EST FERMÉ ET SES PIÈCES EFFACÉES.
/// </summary>
public sealed class UserAnonymizedSellerPurgeHandler
    : IIntegrationEventHandler<UserAnonymizedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
    private const string ConsumerName = "seller-service.identity-user-anonymized";

    private readonly ISellerRepository _sellers;
    private readonly IConsumerInbox _inbox;
    private readonly ISellerUnitOfWork _unitOfWork;
    private readonly ILogger<UserAnonymizedSellerPurgeHandler> _logger;

    public UserAnonymizedSellerPurgeHandler(
        ISellerRepository sellers,
        IConsumerInbox inbox,
        ISellerUnitOfWork unitOfWork,
        ILogger<UserAnonymizedSellerPurgeHandler> logger)
    {
        _sellers = sellers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        UserAnonymizedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // GARDE D'IDEMPOTENCE DU §19.5 — ET ELLE EST LOAD-BEARING ICI.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var seller = await _sellers.GetByUserIdAsync(e.UserId, cancellationToken);

        if (seller is null)
        {
            // LE CAS NORMAL, ET DE LOIN LE PLUS FRÉQUENT.
            await MarquerTraiteAsync(e, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // L'ORDRE COMPTE : ON FERME AVANT DE PURGER.
        var fermeture = seller.RequestClosure();

        if (fermeture.IsFailure)
        {
            _logger.LogInformation(
                "Vendeur {SellerId} déjà hors activité ({Code}) : seule la purge des pièces s'applique.",
                seller.Id.Value, fermeture.Error.Code);
        }

        // Nomme chaque pièce à effacer, une par une.
        var pieces = seller.KybDocuments.Count;
        seller.MarkForDeletion();

        await MarquerTraiteAsync(e, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // ON NE JOURNALISE NI L'IDENTIFIANT DU COMPTE, NI RIEN QUI LE DÉSIGNE.
        _logger.LogInformation(
            "Compte anonymisé : vendeur {SellerId} fermé, {Count} pièce(s) KYB marquée(s) pour effacement.",
            seller.Id.Value, pieces);
    }

    private Task MarquerTraiteAsync(UserAnonymizedIntegrationEvent e, CancellationToken cancellationToken)
        => _inbox.MarkProcessedAsync(
            e.Id,
            ConsumerName,
            "identity.user.anonymized",
            HbaRequestContext.Current.CorrelationId,
            cancellationToken);
}
