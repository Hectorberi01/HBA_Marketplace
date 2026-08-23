using HBA.Engagement.Reviews.Contracts.IntegrationEvents;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

// ═════════════════════════════════════════════════════════════════════════════
// CE FICHIER VIT DANS `Infrastructure/Integration`, ET NON DANS `Application`.
//
// Il dépend de `IConsumerInbox`, qui vit dans `HBA.Shared.Infrastructure` — que la
// couche Application ne référence pas, délibérément. Même arbitrage que pour
// `UserAnonymizedSellerPurgeHandler`.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Merchants.Infrastructure.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA NOTE DU VENDEUR, ENFIN ALIMENTÉE.
///
/// `Seller.UpdateRating` N'AVAIT AUCUN APPELANT DANS TOUT LE DÉPÔT.
///
/// La colonne existait, était persistée, figurait dans la projection de vitrine —
/// et valait `0` pour tout le monde. Un vendeur ayant écoulé trois cents commandes
/// était présenté comme n'ayant jamais vendu ni satisfait personne. Sur une place
/// de marché, la preuve sociale sur laquelle repose l'achat était constamment
/// fausse, et fausse dans le sens qui décourage.
///
/// ON POSE LA VALEUR REÇUE, ON N'ACCUMULE RIEN.
///
/// `SellerRatingRecomputedIntegrationEvent` porte la MOYENNE recalculée par
/// review-service depuis ses propres tables, pas l'avis qui vient d'arriver.
/// Recevoir deux fois le même message pose donc deux fois la même valeur : le
/// gestionnaire est idempotent par nature, contrairement à celui du RGPD dont la
/// garde d'inbox est load-bearing.
///
/// La garde est quand même là — ceinture et bretelles — et elle sert surtout à ne
/// pas réécrire, à chaque rejeu, une ligne que rien ne change.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerRatingHandler
    : IIntegrationEventHandler<SellerRatingRecomputedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
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
            //
            // Un avis peut viser un produit dont le vendeur a depuis été supprimé.
            // Lever ferait rejouer trois fois un message qui ne réussira jamais,
            // puis journaliser en Critical une perte qui n'en est pas une. Sans la
            // trace, l'événement reviendrait à chaque rejeu pour ne rien faire.
            await MarquerTraiteAsync(e, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // `double` → `decimal`, ET LA BORNE EST DÉJÀ DANS L'AGRÉGAT.
        //
        // `UpdateRating` refuse hors [0, 5]. On ne recopie pas ce contrôle ici :
        // deux bornes pour une règle divergent au premier ajustement.
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
