using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HBA.Inventory.Application.Stock.Commands;
using HBA.Inventory.Contracts;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Infrastructure.Persistence;

namespace HBA.Inventory.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Inventory.</summary>
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
        // une saisie GPS ne survivait pas à sa propre relecture.
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
        // ICI, ET ICI SEULEMENT, ON LAISSE PASSER — ET C'EST UN CHOIX DE
        // TRANSITION.
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
