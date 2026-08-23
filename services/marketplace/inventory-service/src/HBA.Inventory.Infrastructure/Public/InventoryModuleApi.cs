using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HBA.Inventory.Application.Stock.Commands;
using HBA.Inventory.Contracts;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Infrastructure.Persistence;

namespace HBA.Inventory.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Inventory.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SKU SANS LIGNE DE STOCK ÉTAIT VENDABLE SANS LIMITE (ISSUE-046).
///
/// Les trois méthodes de ce fichier partageaient la même règle implicite : « pas
/// d'enregistrement de stock = article non suivi = disponible ». Elle s'écrivait
/// `return true;`, trois fois, avec un commentaire rassurant.
///
/// Ce qu'elle produisait, bout à bout : le panier acceptait n'importe quelle
/// quantité, la commande « réservait avec succès » sans rien réserver, et la
/// confirmation « soldait » sans rien décrémenter. Une offre dont le vendeur
/// n'avait jamais saisi de stock se vendait donc **à l'infini**, et rien dans le
/// système ne pouvait le signaler — puisque de son point de vue tout allait bien.
///
/// Or l'offre ne porte AUCUNE quantité à elle : `ProductOffer` n'a pas de stock,
/// seulement un statut `OutOfStock` que quelqu'un d'autre doit poser. Inventory
/// est la SEULE source. Une absence de ligne n'y voulait donc pas dire « article
/// non géré » — elle voulait dire « personne ne sait combien il y en a ».
///
/// LA RÈGLE EST INVERSÉE : PAS DE LIGNE DE STOCK, PAS DE VENTE.
///
/// Décision prise le 31 août 2026. Le coût est assumé et il est immédiat : une
/// offre dont le vendeur n'a pas saisi de stock CESSE de se vendre. C'est le prix
/// d'un compteur qui dit la vérité, et l'inverse — vendre ce qu'on n'a peut-être
/// pas — se paie en commandes qu'on ne peut pas honorer, une par une, après
/// encaissement.
///
/// CE QUI RESTE À FAIRE POUR QUE CE CHOIX SOIT TENABLE.
///
/// La création d'une ligne de stock doit devenir OBLIGATOIRE dans le parcours
/// vendeur — à la publication d'une offre, pas après. Tant que ce n'est pas fait,
/// un vendeur peut publier une offre invendable sans qu'on le lui dise. C'est une
/// correction de catalog-service, elle n'est pas dans ce lot, et elle est nommée
/// dans `RESTE_A_FAIRE.md`.
///
/// LA RESTAURATION N'EST PAS CONCERNÉE — VÉRIFIÉ, PAS SUPPOSÉ.
///
/// Une ligne de repas porte un SKU vide et ne passe jamais par ces méthodes :
/// `RequiresStockReservation` l'écarte dans les trois boucles d'order-service, et
/// restaurant-service n'appelle de ce contrat que `GetLocationAsync`. Le
/// durcissement ne ferme donc aucune commande de repas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class InventoryModuleApi : IInventoryModuleApi
{
    private readonly InventoryDbContext _dbContext;
    private readonly ISender _sender;
    private readonly ILogger<InventoryModuleApi> _logger;

    public InventoryModuleApi(InventoryDbContext dbContext, ISender sender, ILogger<InventoryModuleApi> logger)
    {
        _dbContext = dbContext;
        _sender = sender;
        _logger = logger;
    }

    public async Task<FulfillmentLocationSummary?> GetLocationAsync(
        Guid locationId, CancellationToken cancellationToken = default)
    {
        var location = await _dbContext.FulfillmentLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == new Domain.Locations.FulfillmentLocationId(locationId), cancellationToken);

        if (location is null)
        {
            return null;
        }

        // Latitude et Longitude sont bien RECOPIÉES. La projection jumelle de
        // ListFulfillmentLocationsQuery les écrasait autrefois par « null, null » :
        // une saisie GPS ne survivait pas à sa propre relecture. Une projection
        // écrite à un second endroit est l'occasion parfaite de refaire la même
        // erreur — d'où ce rappel plutôt qu'un simple mapping muet.
        return new FulfillmentLocationSummary(
            location.Id.Value,
            location.Type.ToString(),
            location.OwnerId,
            location.Address.CommuneCode,
            location.Address.CommuneName,
            location.Address.Quartier,
            location.Address.Landmark,
            location.Address.Line,
            location.Address.CountryCode,
            location.Address.Latitude,
            location.Address.Longitude,
            location.Address.ContactPhone);
    }

    public async Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return new AvailabilitySummary(sku, 0);
        }

        var value = skuResult.Value;
        var items = await _dbContext.InventoryItems
            .AsNoTracking()
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);

        return new AvailabilitySummary(value.Value, items.Sum(i => i.Available));
    }

    public async Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        var items = await _dbContext.InventoryItems
            .AsNoTracking()
            .Include(i => i.Reservations)
            .Where(i => i.Sku == value)
            .ToListAsync(cancellationToken);

        // AUCUNE LIGNE DE STOCK = PAS VENDABLE (ISSUE-046, voir l'encadré).
        //
        // Cette branche rendait `true`. C'est ce `true` qui laissait un article
        // sans stock connu entrer dans un panier en quantité illimitée.
        if (items.Count == 0)
        {
            _logger.LogWarning(
                "SKU {Sku} demandé ({Quantity}) sans AUCUNE ligne de stock : refusé. "
                + "L'offre correspondante est invendable tant que son vendeur n'a pas saisi de stock.",
                sku, quantity);

            return false;
        }

        return items.Sum(i => i.Available) >= quantity;
    }

    public async Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
    {
        // ON NE RÉSERVE PLUS « AVEC SUCCÈS » CE QU'ON NE SUIT PAS (ISSUE-046).
        //
        // Cette branche rendait `true` sans rien réserver. La saga de commande
        // enregistrait donc une réservation qui n'existait pas, et la ligne
        // partait au paiement puis à l'expédition sur un stock inconnu.
        if (!await HasStockRecordAsync(sku, locationId, cancellationToken))
        {
            _logger.LogWarning(
                "Commande {OrderId} : réservation refusée pour le SKU {Sku} sur l'emplacement "
                + "{LocationId} — aucune ligne de stock. Réserver « avec succès » sans rien réserver "
                + "reviendrait à vendre une quantité que personne ne connaît.",
                orderId, sku, locationId);

            return false;
        }

        var result = await _sender.Send(new ReserveStockCommand(sku, locationId, orderId, quantity), cancellationToken);
        return result.IsSuccess;
    }

    public async Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await HasStockRecordAsync(sku, locationId, cancellationToken))
        {
            return; // rien à libérer pour un SKU non suivi
        }

        await _sender.Send(new ReleaseReservationCommand(sku, locationId, orderId), cancellationToken);
    }

    public async Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        // ICI, ET ICI SEULEMENT, ON LAISSE PASSER — ET C'EST UN CHOIX DE TRANSITION.
        //
        // Depuis le durcissement de `TryReserveAsync`, aucune commande NOUVELLE ne
        // peut atteindre la confirmation avec un SKU sans stock : elle aurait été
        // refusée à la réservation. Ne restent que les commandes DÉJÀ EN VOL au
        // moment du déploiement — réservées sous l'ancienne règle, et payées.
        //
        // Refuser leur confirmation bloquerait une commande encaissée : le client
        // a payé, le vendeur ne serait jamais crédité, et l'escrow ne serait
        // jamais levé. On laisse donc passer, et on le DIT en `Critical` — c'est
        // du stock qui sort sans être décrémenté nulle part, et cela demande une
        // reprise manuelle.
        //
        // Cette branche doit disparaître quand la file des commandes antérieures
        // au déploiement est vidée.
        if (!await HasStockRecordAsync(sku, locationId, cancellationToken))
        {
            _logger.LogCritical(
                "Commande {OrderId} : confirmation du SKU {Sku} sur l'emplacement {LocationId} SANS "
                + "ligne de stock. Aucun décrément n'est possible — commande antérieure au "
                + "durcissement d'ISSUE-046. La marchandise sort sans que le stock en porte la trace : "
                + "reprise manuelle requise.",
                orderId, sku, locationId);

            return true;
        }

        var result = await _sender.Send(new ConfirmReservationCommand(sku, locationId, orderId), cancellationToken);
        return result.IsSuccess;
    }

    /// <summary>Existe-t-il un enregistrement de stock pour ce SKU à cette localisation ?</summary>
    private async Task<bool> HasStockRecordAsync(string sku, Guid locationId, CancellationToken cancellationToken)
    {
        var skuResult = Sku.Create(sku);
        if (skuResult.IsFailure)
        {
            return false;
        }

        var value = skuResult.Value;
        return await _dbContext.InventoryItems
            .AsNoTracking()
            .AnyAsync(i => i.Sku == value && i.LocationId == locationId, cancellationToken);
    }
}
