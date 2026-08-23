using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Application.Offers;
using HBA.Catalog.Domain.Offers;
using HBA.Merchants.Contracts.IntegrationEvents;

// ═════════════════════════════════════════════════════════════════════════════
// SUSPENDRE UN VENDEUR NE RETIRAIT RIEN DE LA VENTE (ISSUE-025).
//
// `SellerSuspendedIntegrationEvent` était publié correctement — y compris par un
// REFUS DE DOSSIER KYB, qui suspend le vendeur s'il était actif (`Seller.cs`,
// `RejectKyb`). Il n'avait qu'un seul consommateur : une notification. Le vendeur
// recevait donc un message lui annonçant sa suspension, et ses offres restaient
// achetables. Un client pouvait commander chez un vendeur refusé au KYB.
//
// Tout ce qu'il fallait existait déjà et n'avait AUCUN appelant : le marqueur
// `SellerCatalogSuspension`, écrit avec son encadré, et
// `IProductOfferRepository.ListAllBySellerForUpdateAsync`, documentée « par
// vendeur, pas par boutique » précisément pour ce cas. Ce fichier est le
// raccordement manquant, rien de plus.
//
// LES OFFRES, PAS LES FICHES PRODUIT. LE CHOIX EST DOCUMENTÉ AILLEURS.
//
// L'encadré de `SellerCatalogSuspension` l'écrit déjà : le volet « fiche » exige
// d'abord de trancher un modèle. La raison concrète est visible dans
// `Product.Restore()`, qui rend la fiche à `Approved` et NON à `Published` —
// « c'est le vendeur qui décide de la remettre en vente ». Suspendre les fiches
// obligerait donc chaque vendeur réhabilité à republier tout son catalogue à la
// main, ce qui transformerait une mesure conservatoire en sanction durable.
//
// Ce n'est pas une perte : c'est l'OFFRE qui est achetable. Le panier porte des
// identifiants d'offre, la Buy Box lit `ListActiveByProductAsync`, et
// `OfferStatusTransitions.IsPurchasable` ne rend vrai que pour `Active`. Une
// offre suspendue n'entre pas dans une commande.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Catalog.Infrastructure.Integration;

/// <summary>
/// Le vendeur est suspendu : ses offres sortent de la vente.
/// </summary>
public sealed class SellerSuspendedOfferWithdrawalHandler
    : IIntegrationEventHandler<SellerSuspendedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
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
        // trompeur. Ce gestionnaire ÉCRIT le motif, et le motif porte l'état
        // d'avant. Un rejeu après une levée partielle recomposerait un motif à
        // partir d'un état déjà relevé : l'offre garderait pour toujours la trace
        // d'un « avant » qui n'a jamais existé.
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            _logger.LogDebug(
                "Événement {EventId} déjà traité par {Consumer} : ignoré.", e.Id, ConsumerName);

            return;
        }

        var offres = await _offers.ListAllBySellerForUpdateAsync(e.SellerId, cancellationToken);

        // Même filtre que la fermeture de boutique. `Draft` est déjà invisible et
        // `Archived` est terminal : les suspendre n'aurait aucun effet et leur
        // poserait un motif que la levée devrait ensuite défaire.
        var aRetirer = offres
            .Where(o => o.Status is OfferStatus.Active or OfferStatus.OutOfStock or OfferStatus.Paused)
            .ToList();

        var retirees = 0;
        foreach (var offre in aRetirer)
        {
            // LE MOTIF EST COMPOSÉ AVANT L'APPEL, PAS DEDANS.
            //
            // `Suspend` écrase `Status`. Lire `offre.Status` en argument d'un appel
            // qui le modifie fonctionne aujourd'hui — les arguments sont évalués
            // d'abord — mais c'est le genre de dépendance à l'ordre d'évaluation
            // qu'une refonte casse sans bruit. La variable locale la rend explicite.
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
        //
        // Sans lui, la levée relèverait AUSSI une offre qu'un modérateur avait
        // suspendue pour contrefaçon ou prix aberrant, et une offre retirée par la
        // fermeture d'une boutique encore fermée. Le vendeur obtiendrait, en prime
        // de sa réhabilitation, l'annulation de sanctions sans rapport — sans que
        // personne ne le voie : l'offre redeviendrait simplement achetable.
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
            //
            // `ChangeStatus(Active, reason: null)` remet `StatusReason` à null. Lire
            // l'état d'avant après l'appel rendrait toujours `Active` — donc
            // remettrait en vente, à chaque levée, des offres qui étaient en rupture.
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
