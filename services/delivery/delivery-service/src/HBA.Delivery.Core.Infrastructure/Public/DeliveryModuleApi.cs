using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Contracts;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Deliveries.Infrastructure.Public;

/// <summary>Implémentation en processus de <see cref="IDeliveryModuleApi"/>.</summary>
internal sealed class DeliveryModuleApi : IDeliveryModuleApi
{
    private readonly DeliveriesDbContext _dbContext;
    private readonly IDriverLocationCache _locations;

    public DeliveryModuleApi(DeliveriesDbContext dbContext, IDriverLocationCache locations)
    {
        _dbContext = dbContext;
        _locations = locations;
    }

    public async Task<DeliverySummary?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var id = new DeliveryId(deliveryId);
        var delivery = await _dbContext.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        return delivery is null ? null : await ToSummaryAsync(delivery, cancellationToken);
    }

    public async Task<DriverAccount?> GetDriverAccountAsync(
        Guid driverId, CancellationToken cancellationToken = default)
    {
        var id = new DriverId(driverId);

        // Projection au plus juste : cette lecture est sur le chemin de la
        // notification de proposition, qui dispose de quarante-cinq secondes en
        // tout.
        return await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DriverAccount(d.Id.Value, d.UserId, d.FullName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DeliverySummary?> GetByReferenceAsync(
        string reference, string source, CancellationToken cancellationToken = default)
    {
        // La source arrive en TEXTE : c'est le contrat public, et un appelant
        // externe n'a pas à connaître nos valeurs numériques.
        if (!Enum.TryParse<DeliverySource>(source, ignoreCase: true, out var parsed))
        {
            return null;
        }

        var delivery = await _dbContext.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Reference == reference && d.Source == parsed, cancellationToken);

        return delivery is null ? null : await ToSummaryAsync(delivery, cancellationToken);
    }

    public async Task<DeliveryTracking?> GetTrackingAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var id = new DeliveryId(deliveryId);
        var delivery = await _dbContext.Deliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (delivery is null)
        {
            return null;
        }

        var driver = await LoadDriverAsync(delivery, cancellationToken);

        // LA POSITION N'EST EXPOSÉE QUE PENDANT LE TRANSPORT.
        var inTransit = delivery.Status is DeliveryStatus.DriverAccepted
            or DeliveryStatus.ArrivedAtPickup
            or DeliveryStatus.PickedUp
            or DeliveryStatus.InTransit
            or DeliveryStatus.ArrivedAtDropoff;

        DriverPosition? position = null;
        if (inTransit && delivery.AssignedDriverId is { } assigned)
        {
            position = await _locations.GetAsync(assigned, cancellationToken);
        }

        return new DeliveryTracking(
            delivery.Id.Value,
            delivery.Status.ToString(),
            position?.Position.Latitude,
            position?.Position.Longitude,
            position?.ReportedAtUtc,
            driver?.FullName,
            driver?.Phone);
    }

    private async Task<DeliverySummary> ToSummaryAsync(
        Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken)
    {
        var driver = await LoadDriverAsync(delivery, cancellationToken);

        return new DeliverySummary(
            delivery.Id.Value,
            delivery.Reference,
            delivery.Source.ToString(),
            delivery.Type.ToString(),
            delivery.Status.ToString(),
            delivery.Pickup.Summary,
            delivery.Dropoff.Summary,
            driver?.FullName,
            driver?.Phone,
            delivery.CreatedAtUtc,
            delivery.AcceptedAtUtc,
            delivery.PickedUpAtUtc,
            delivery.DeliveredAtUtc);
    }

    private async Task<Domain.Drivers.Driver?> LoadDriverAsync(
        Domain.Deliveries.Delivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.AssignedDriverId is not { } assigned)
        {
            return null;
        }

        return await _dbContext.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == assigned, cancellationToken);
    }
}
