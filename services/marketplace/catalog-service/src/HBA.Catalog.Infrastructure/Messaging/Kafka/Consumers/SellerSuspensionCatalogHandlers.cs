using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Application.Offers;
using HBA.Catalog.Domain.Offers;
using HBA.Merchants.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Catalog.Infrastructure;

using HBA.Catalog.Infrastructure.Persistence.Outbox;
using HBA.Catalog.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
// SUSPENDRE UN VENDEUR NE RETIRAIT RIEN DE LA VENTE (ISSUE-025).

namespace HBA.Catalog.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le vendeur est suspendu : ses offres sortent de la vente.</summary>
public sealed class SellerSuspendedOfferWithdrawalHandler
    : IIntegrationEventHandler<SellerSuspendedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
    private const string ConsumerName = "catalog-service.merchants-seller-suspended";

    private readonly IProductOfferRepository _offers;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerSuspendedOfferWithdrawalHandler> _logger;

    public SellerSuspendedOfferWithdrawalHandler(
        IProductOfferRepository offers,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerSuspendedOfferWithdrawalHandler> logger)
    {
        _offers = offers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // LA GARDE D'IDEMPOTENCE COMPTE VRAIMENT ICI, contrairement à
        // `SellerClosedProductInvalidationHandler` où elle n'évitait qu'un journal
        // trompeur.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var offres = await _offers.ListAllBySellerForUpdateAsync(e.SellerId, cancellationToken);

        // Même filtre que la fermeture de boutique.
        var aRetirer = offres
            .Where(o => o.Status is OfferStatus.Active or OfferStatus.OutOfStock or OfferStatus.Paused)
            .ToList();

        var retirees = 0;
        foreach (var offre in aRetirer)
        {
            // LE MOTIF EST COMPOSÉ AVANT L'APPEL, PAS DEDANS.
            var motif = SellerCatalogSuspension.ComposeReason(e.Reason, offre.Status);

            if (offre.Suspend(motif).IsSuccess)
            {
                retirees++;
            }
        }

        // La trace d'inbox et l'effet partent dans la MÊME unité de travail :
        // committer séparément rouvrirait la fenêtre que l'inbox ferme.
        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.seller.suspended",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Vendeur {SellerId} suspendu : {Offres} offre(s) retirée(s) de la vente.",
            e.SellerId, retirees);
    }
}

/// <summary>
/// La suspension est levée : les offres retirées PAR CETTE SUSPENSION reviennent,
/// et rien d'autre.
/// </summary>
public sealed class SellerSuspensionLiftedOfferReinstatementHandler
    : IIntegrationEventHandler<SellerSuspensionLiftedIntegrationEvent>
{
    private const string ConsumerName = "catalog-service.merchants-seller-suspension-lifted";

    private readonly IProductOfferRepository _offers;
    private readonly IConsumerInbox _inbox;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerSuspensionLiftedOfferReinstatementHandler> _logger;

    public SellerSuspensionLiftedOfferReinstatementHandler(
        IProductOfferRepository offers,
        IConsumerInbox inbox,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerSuspensionLiftedOfferReinstatementHandler> logger)
    {
        _offers = offers;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerSuspensionLiftedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var offres = await _offers.ListAllBySellerForUpdateAsync(e.SellerId, cancellationToken);

        // LE FILTRE SUR LE MOTIF EST TOUT L'INTÉRÊT DU MARQUEUR.
        var candidates = offres
            .Where(o => o.Status == OfferStatus.Suspended
                        && SellerCatalogSuspension.IsSellerSuspension(o.StatusReason))
            .ToList();

        var remises = 0;
        var enRupture = 0;
        var enPause = 0;

        foreach (var offre in candidates)
        {
            // LU AVANT `Activate()`, QUI EFFACE LE MOTIF.
            var avant = SellerCatalogSuspension.ReadPreviousStatus(offre.StatusReason);

            // La liste blanche des transitions n'autorise que `Suspended -> Active`
            // et `Suspended -> Archived`. On repasse donc par `Active`, puis on
            // redescend — chaque saut est une transition légale, et chacun lève son
            // propre événement de domaine.
            if (offre.Activate().IsFailure)
            {
                continue;
            }

            remises++;

            switch (avant)
            {
                case OfferStatus.OutOfStock when offre.MarkOutOfStock().IsSuccess:
                    enRupture++;
                    break;

                case OfferStatus.Paused when offre.Pause().IsSuccess:
                    enPause++;
                    break;
            }
        }

        await _inbox.MarkProcessedAsync(
            e.Id, ConsumerName, "merchants.seller.suspension-lifted",
            HbaRequestContext.Current.CorrelationId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Suspension du vendeur {SellerId} levée : {Remises} offre(s) relevée(s), "
            + "dont {Rupture} remise(s) en rupture et {Pause} en pause.",
            e.SellerId, remises, enRupture, enPause);
    }
}
