using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Inventory.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Catalog.Infrastructure.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE STOCK DÉCIDE DE LA MISE EN VENTE — ET PERSONNE NE L'ÉCOUTAIT (ISSUE-047).
///
/// AUCUNE OFFRE N'EST JAMAIS PASSÉE `OutOfStock`, NI N'EST JAMAIS REVENUE EN
/// VENTE.
///
/// Tout était en place sauf le maillon :
///
///   • `OfferStatus.OutOfStock` existe, avec ses six transitions autorisées ;
///   • `ProductOffer.MarkOutOfStock()` existe ;
///   • `MarkOfferOutOfStockCommand` existe — et n'a AUCUN émetteur ;
///   • `StockDepletedIntegrationEvent` et `StockReplenishedIntegrationEvent` sont
///     publiés depuis toujours par inventory-service ;
///   • `IProductOfferRepository.ListBySkuAsync` a été écrite POUR CE CAS — son
///     commentaire le dit en toutes lettres : « Inventory s'en sert pour signaler
///     une rupture » ;
///   • le contrat d'inventaire annonce lui aussi « consommé par Offers pour
///     passer l'offre OutOfStock » et « consommé par le composition root pour
///     relancer les offres ».
///
/// Six affirmations, dans cinq fichiers, décrivant un chemin que rien ne
/// parcourait. catalog-service n'enregistrait AUCUN consommateur d'événement de
/// stock, et le répartiteur résout paresseusement : un événement sans
/// gestionnaire est marqué traité et disparaît, sans erreur ni avertissement.
///
/// CE QUE CELA COÛTAIT, DANS LES DEUX SENS.
///
/// Une offre en rupture restait ACHETABLE : l'acheteur commandait, la réservation
/// de stock échouait au checkout, et il découvrait l'indisponibilité après avoir
/// choisi son adresse. Dans l'autre sens, un réassort ne remettait rien en
/// vente — le vendeur devait s'en apercevoir et relancer chaque offre à la main.
///
/// ET LE NOM DE LA SECONDE CLASSE N'EST PAS UN HASARD.
///
/// `ReactivateOffersOnStockReplenishedHandler` était déjà cité, par son nom, dans
/// `OfferStatus.cs` — pour justifier la transition `OutOfStock → Suspended` :
/// « puis le stock remontait, ReactivateOffersOnStockReplenishedHandler la
/// repassait en Active — et le vendeur écarté par la plateforme revendait ». Un
/// raisonnement juste, sur une classe qui n'existait pas. On lui rend son nom.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // SEULES LES OFFRES `Active` SONT RETIRÉES, ET SEULES CELLES DU LIEU
        //    CONCERNÉ.
        //
        // Le filtre sur le LIEU : `InventoryItem` porte une ligne par (SKU, lieu),
        // et `ProductOffer.ShipFromLocationId` dit depuis quel lieu cette offre
        // expédie. Un vendeur qui tient le même SKU dans deux entrepôts n'a pas de
        // rupture globale quand l'un se vide — retirer les deux offres lui
        // supprimerait des ventes qu'il peut honorer.
        //
        // Le filtre sur `Active` : une offre `Paused` par son vendeur, `Suspended`
        // par la plateforme ou `Archived` ne doit pas changer d'état parce que le
        // stock bouge. Passer une offre suspendue en `OutOfStock` effacerait la
        // sanction — et la liste blanche des transitions refuserait de toute façon
        // `Suspended → OutOfStock`.
        // ═════════════════════════════════════════════════════════════════════
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
/// Le stock remonte : les offres que LA RUPTURE avait retirées reviennent, et
/// rien d'autre.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// « ET RIEN D'AUTRE » EST TOUTE LA GARDE.
///
/// Le filtre sur `OutOfStock` n'est pas une commodité : c'est ce qui empêche un
/// réassort de lever une sanction. `OfferStatus.cs` documente exactement ce
/// scénario, et l'a même corrigé en amont en autorisant `OutOfStock → Suspended` —
/// pour qu'une offre en rupture PUISSE être suspendue, et ne revienne donc pas ici.
///
/// Une offre `Paused` par son vendeur reste en pause : c'est sa décision, pas
/// celle du stock. Une offre `Suspended` reste suspendue. Une offre `Archived` est
/// terminale.
///
/// CE QUE CE GESTIONNAIRE NE COUVRE PAS.
///
/// Il ne distingue pas « retirée par la rupture d'AUJOURD'HUI » de « retirée par
/// une rupture d'il y a six mois ». Toute offre `OutOfStock` de ce SKU et de ce
/// lieu revient en vente dès que le stock remonte, ce qui est le comportement
/// voulu — mais cela signifie qu'une offre qu'un vendeur aurait laissée en
/// rupture volontairement (en vidant son stock pour la retirer) réapparaîtra au
/// premier réassort. Le geste correct pour retirer durablement une offre est
/// `Pause`, et rien dans l'interface ne le dit encore.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
