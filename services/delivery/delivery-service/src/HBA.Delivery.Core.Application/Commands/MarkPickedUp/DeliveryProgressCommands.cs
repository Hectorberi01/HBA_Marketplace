using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Commands;

// LES ÉTAPES D'EXÉCUTION PORTENT DÉSORMAIS « RequiredDriverId ».

/// <summary>Le livreur est arrivé au point de collecte.</summary>
public sealed record MarkArrivedAtPickupCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>Le colis est pris en charge.</summary>
public sealed record MarkPickedUpCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>En route vers le destinataire.</summary>
public sealed record MarkInTransitCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>Le livreur est arrivé chez le destinataire.</summary>
public sealed record MarkArrivedAtDropoffCommand(Guid DeliveryId, Guid? RequiredDriverId = null) : ICommand;

/// <summary>
/// Remise effectuée. <paramref name="ProofValue"/> n'est exigé que si la course a
/// été créée avec une preuve requise — c'est l'agrégat qui tranche.
/// </summary>
public sealed record MarkDeliveredCommand(
    Guid DeliveryId, string? ProofValue = null, Guid? RequiredDriverId = null) : ICommand;

/// <summary>Annule la course. Impossible une fois le colis collecté.</summary>
/// <summary>Annule une course.</summary>
public sealed record CancelDeliveryCommand(
    Guid DeliveryId, string? Reason, Guid? RequiredPartnerId) : ICommand;

/// <summary>LES TRANSITIONS D'EXÉCUTION, TOUTES SUR LE MÊME MOULE.</summary>
internal sealed class DeliveryProgressCommandHandler
    : ICommandHandler<MarkArrivedAtPickupCommand>,
      ICommandHandler<MarkPickedUpCommand>,
      ICommandHandler<MarkInTransitCommand>,
      ICommandHandler<MarkArrivedAtDropoffCommand>,
      ICommandHandler<MarkDeliveredCommand>,
      ICommandHandler<CancelDeliveryCommand>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryPayoutSettings _payout;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public DeliveryProgressCommandHandler(
        IDeliveryRepository deliveries,
        IDriverRepository drivers,
        IDeliveryPayoutSettings payout,
        IDeliveryUnitOfWork unitOfWork)
    {
        _deliveries = deliveries;
        _drivers = drivers;
        _payout = payout;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(MarkArrivedAtPickupCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkArrivedAtPickup(), ct);

    public Task<Result> Handle(MarkPickedUpCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkPickedUp(), ct);

    public Task<Result> Handle(MarkInTransitCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkInTransit(), ct);

    public Task<Result> Handle(MarkArrivedAtDropoffCommand c, CancellationToken ct)
        => MutateAsync(c.DeliveryId, c.RequiredDriverId, d => d.MarkArrivedAtDropoff(), ct);

    public async Task<Result> Handle(CancelDeliveryCommand c, CancellationToken ct)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(c.DeliveryId), ct);

        // « Introuvable » et non « interdit » : un 403 confirmerait au demandeur
        // que la course existe et appartient à quelqu'un d'autre.
        if (delivery is null
            || (c.RequiredPartnerId is not null && delivery.PartnerId != c.RequiredPartnerId))
        {
            return NotFound();
        }

        var cancelled = delivery.Cancel(c.Reason);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Remise : la course se ferme ET le livreur se libère.</summary>
    public async Task<Result> Handle(MarkDeliveredCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        // Même garde que les autres étapes — et c'est ICI qu'elle compte le plus :
        // c'est cette transition qui déclenche le gain du livreur.
        if (command.RequiredDriverId is not null
            && delivery.AssignedDriverId?.Value != command.RequiredDriverId)
        {
            return NotFound();
        }

        // On retient le livreur AVANT la transition : l'agrégat conserve son
        // identifiant après la remise, mais s'appuyer sur cet ordre reviendrait à
        // dépendre d'un détail interne que rien ne garantit.
        var assigned = delivery.AssignedDriverId;

        // Le taux est lu MAINTENANT et figé sur la course.
        var delivered = delivery.MarkDelivered(command.ProofValue, _payout.DriverShareRate);
        if (delivered.IsFailure)
        {
            // ON ENREGISTRE MÊME QUAND LA REMISE ÉCHOUE.
            if (delivered.Error.Code is "delivery.proof.pin_mismatch")
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return delivered;
        }

        if (assigned is { } driverId)
        {
            var driver = await _drivers.GetByIdAsync(driverId, cancellationToken);

            // Un échec ici n'annule PAS la remise : le colis est chez le client,
            // c'est un fait acquis.
            driver?.CompleteMission();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> MutateAsync(
        Guid deliveryId,
        Guid? requiredDriverId,
        Func<Domain.Deliveries.Delivery, Result> mutate,
        CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(deliveryId), cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        // « Introuvable » et non « interdit » : un 403 confirmerait au livreur
        // qu'une course existe et qu'elle est confiée à quelqu'un d'autre.
        if (requiredDriverId is not null && delivery.AssignedDriverId?.Value != requiredDriverId)
        {
            return NotFound();
        }

        var result = mutate(delivery);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Result NotFound()
        => Result.Failure(Error.NotFound("delivery.not_found", "Course introuvable."));
}
