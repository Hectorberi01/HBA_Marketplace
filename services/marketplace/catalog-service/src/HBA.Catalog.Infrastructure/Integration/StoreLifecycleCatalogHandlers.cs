using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Application.Offers;
using HBA.Merchants.Contracts.IntegrationEvents;

// ═════════════════════════════════════════════════════════════════════════════
// FERMER UNE BOUTIQUE NE RETIRAIT RIEN DE LA VENTE (ISSUE-041).
//
// seller-service publiait consciencieusement les quatre événements du cycle de vie
// d'une boutique — fermeture, réouverture, suspension, levée. Catalog écoutait le
// topic `service.merchant.v1` depuis toujours. Et `SuspendStoreCatalogCommand` /
// `ReinstateStoreCatalogCommand` étaient écrites, avec leurs encadrés, sans
// AUCUN appelant. Le vendeur fermait sa boutique, voyait « Fermée » sur son écran,
// et les commandes continuaient d'arriver.
//
// Ce fichier est le raccordement manquant, et rien d'autre : les trois handlers ne
// contiennent aucune règle. Ils gardent l'idempotence et délèguent aux commandes,
// qui portent la boucle et la transaction.
//
// POURQUOI DÉLÉGUER AUX COMMANDES PLUTÔT QUE REFAIRE LA BOUCLE ICI.
//
// `SellerSuspensionCatalogHandlers`, juste à côté, travaille directement sur le
// dépôt — parce qu'aucune commande n'existait pour ce cas. Ici deux commandes
// existent, complètes et documentées. Les dupliquer laisserait deux chemins pour
// un même effet, dont un seul serait corrigé le jour où la règle changera. C'est
// exactement le reproche que l'audit fait au reste du dépôt.
//
// LA MARQUE D'INBOX EST POSÉE AVANT L'ENVOI, ET CE N'EST PAS UNE INVERSION.
//
// `MarkProcessedAsync` ne fait qu'INSCRIRE la ligne dans le contexte — il ne
// committe pas, délibérément (voir son encadré). C'est le `SaveChangesAsync` de la
// commande, dans la MÊME portée donc le même `DbContext`, qui valide la trace et
// l'effet ensemble. Poser la marque après l'envoi la committrait dans une seconde
// transaction, et une panne entre les deux laisserait l'événement marqué traité
// alors que rien ne l'aurait été.
//
// IL N'Y A PAS DE HANDLER POUR `StoreSuspensionLiftedIntegrationEvent`, ET
// C'EST VOLONTAIRE.
//
// Lever la sanction repasse la boutique en `Closed`, pas en `Open` — c'est le
// vendeur qui rouvre, quand son stock et ses prix sont à jour. Les offres doivent
// donc rester retirées. Un handler qui les relèverait contredirait le domaine ;
// un handler vide ferait croire à un effet. La réouverture, elle, est portée par
// `StoreOpenedIntegrationEvent` ci-dessous.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Catalog.Infrastructure.Integration;

/// <summary>
/// La boutique ferme — décision du vendeur ou de la plateforme, indistinctement :
/// ses offres quittent la vente.
/// </summary>
public sealed class StoreClosedOfferWithdrawalHandler
    : IIntegrationEventHandler<StoreClosedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
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
            //
            // Le `SaveChangesAsync` de la commande n'a pas eu lieu : ni les offres
            // ni la marque d'inbox ne sont en base. Rendre la main normalement
            // ferait marquer l'événement comme traité par le répartiteur, et la
            // boutique resterait ouverte à la vente pour toujours. L'exception
            // laisse le message d'outbox non traité, donc rejoué.
            throw new InvalidOperationException(
                $"Fermeture de la boutique {e.StoreId} non appliquée au catalogue : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Boutique {StoreId} fermée : offres retirées de la vente.", e.StoreId);
    }
}

/// <summary>
/// La plateforme suspend la boutique. Même effet catalogue qu'une fermeture — le
/// catalogue n'a pas à savoir pourquoi — mais un événement distinct, parce qu'un
/// consommateur de vitrine, lui, doit distinguer des congés d'une sanction.
/// </summary>
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
        //
        // La clé de l'inbox est le couple (événement, consumer). Partager un nom
        // entre deux handlers ne pose pas de problème tant que leurs événements
        // portent des identifiants différents — mais le jour où un même événement
        // doit être traité par deux gestionnaires, le second se croirait déjà passé
        // et ne s'exécuterait jamais. Silencieusement.
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
/// La boutique rouvre : les offres retirées PAR CETTE FERMETURE reviennent, et
/// rien d'autre.
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
        //
        // Elle rend les SKU des offres relevées pour qu'un appelant revérifie leur
        // stock auprès d'inventory. Catalog-service ne connaît pas inventory : ce
        // handler ne peut pas le faire, et c'est précisément pour cela que l'état
        // d'avant est inscrit dans le motif de retrait — une offre qui était en
        // rupture y retourne d'elle-même. Le journal garde le compte.
        _logger.LogInformation(
            "Boutique {StoreId} rouverte : {Offres} offre(s) remise(s) en vente.",
            e.StoreId, resultat.Value.Count);
    }
}
