using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Drivers;

// ═════════════════════════════════════════════════════════════════════════════
// LA RÉPONSE DU LIVREUR À UNE PROPOSITION.
//
// `Delivery.AcceptByDriver` N'AVAIT AUCUN APPELANT. Le lot 5.1/5.3 l'a signalé
// après avoir posé dessus un verrou optimiste (`xmin`) et un index unique partiel
// (`ux_deliveries_engaged_driver`) : deux mécanismes de concurrence sur une
// méthode que rien n'appelait jamais. La méthode existait, ses tests existaient,
// et aucune course ne pouvait être acceptée.
//
// LES DEUX AGRÉGATS BOUGENT ENSEMBLE, ET C'EST L'APPLICATION QUI L'ORCHESTRE.
//
// La course passe à « acceptée », le livreur passe « en mission ». Aucun des deux
// n'a le droit de piloter l'autre — c'est écrit sur `Driver.MarkBusy`, public
// pour cette raison précise. L'Unit of Work garantit que les deux partent
// ensemble : oublier de marquer le livreur occupé le laisserait recevoir une
// seconde proposition pendant qu'il roule.
// ═════════════════════════════════════════════════════════════════════════════

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
        //
        // `AcceptByDriver` refuse si la course n'est pas proposée À CE LIVREUR
        // (`CurrentOffer(driverId)`). Recopier ce test au-dessus donnerait deux
        // sources de vérité pour la même règle, et le jour où l'une changerait,
        // l'autre serait oubliée.
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
        //
        // À la remise, le colis est chez le client : c'est un fait acquis, et un
        // livreur mal libéré est un incident d'exploitation. Ici rien n'a encore
        // eu lieu ; laisser la course acceptée par un livreur que la plateforme
        // considère indisponible produirait une course que le dispatch ne
        // reprendrait jamais et que personne ne livrerait.
        var busy = driver.MarkBusy();
        if (busy.IsFailure)
        {
            return busy;
        }

        // C'EST CE `SaveChanges` QUI DÉCLENCHE LES DEUX ARBITRAGES DE LA BASE
        // (D35) : le jeton `xmin` sur la course — trois colonnes de la ligne
        // parente sont écrites, donc il est réellement évalué — et l'index unique
        // partiel qui interdit à un livreur de porter deux courses engagées. Le
        // conflit ressort en 409 par `ServiceExceptionMiddleware`.
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
        // proposition tombée d'elle-même. `ExpireDeliveryOfferCommand` couvre le
        // second cas, et l'agrégat distingue les deux dans son historique
        // d'affectations : un refus interdit de reproposer, une expiration non.
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
