using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Drivers;

// LA RÉPONSE DU LIVREUR À UNE PROPOSITION.

/// <summary>Le livreur accepte la course qu'on lui propose.</summary>
public sealed record AcceptDeliveryCommand(Guid DeliveryId, Guid DriverId) : ICommand;

/// <summary>Le livreur refuse : la course repart en recherche.</summary>
public sealed record DeclineDeliveryCommand(Guid DeliveryId, Guid DriverId, string? Reason) : ICommand;

internal sealed class DriverOfferCommandHandler
    : ICommandHandler<AcceptDeliveryCommand>,
      ICommandHandler<DeclineDeliveryCommand>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public DriverOfferCommandHandler(
        IDeliveryRepository deliveries,
        IDriverRepository drivers,
        IDeliveryUnitOfWork unitOfWork)
    {
        _deliveries = deliveries;
        _drivers = drivers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AcceptDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        var driverId = new DriverId(command.DriverId);

        // LA GARDE D'APPARTENANCE EST DANS L'AGRÉGAT, PAS ICI.
        var accepted = delivery.AcceptByDriver(driverId);
        if (accepted.IsFailure)
        {
            return accepted;
        }

        var driver = await _drivers.GetByIdAsync(driverId, cancellationToken);
        if (driver is null)
        {
            return Result.Failure(Error.NotFound("driver.not_found", "Livreur introuvable."));
        }

        // UN ÉCHEC ICI ANNULE L'ACCEPTATION, contrairement à la remise.
        var busy = driver.MarkBusy();
        if (busy.IsFailure)
        {
            return busy;
        }

        // C'EST CE `SaveChanges` QUI DÉCLENCHE LES DEUX ARBITRAGES DE LA BASE (D35)
        // : le jeton `xmin` sur la course — trois colonnes de la ligne parente sont
        // écrites, donc il est réellement évalué — et l'index unique partiel qui
        // interdit à un livreur de porter deux courses engagées.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(DeclineDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        // `expired: false` — c'est un REFUS explicite du livreur, pas une
        // proposition tombée d'elle-même.
        var declined = delivery.RejectByDriver(new DriverId(command.DriverId), command.Reason, expired: false);
        if (declined.IsFailure)
        {
            return declined;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result NotFound()
        => Result.Failure(Error.NotFound("delivery.not_found", "Course introuvable."));
}
