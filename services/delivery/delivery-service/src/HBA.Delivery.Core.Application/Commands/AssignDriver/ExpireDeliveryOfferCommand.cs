using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Application.Dispatch;

public sealed record ExpireDeliveryOfferCommand(Guid DeliveryId, Guid DriverId) : ICommand;

internal sealed class ExpireDeliveryOfferCommandHandler : ICommandHandler<ExpireDeliveryOfferCommand>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public ExpireDeliveryOfferCommandHandler(IDeliveryRepository deliveries, IDeliveryUnitOfWork unitOfWork)
    {
        _deliveries = deliveries;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ExpireDeliveryOfferCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return Result.Failure(Error.NotFound("delivery.not_found", "Course introuvable."));
        }

        var result = delivery.RejectByDriver(new DriverId(command.DriverId), reason: null, expired: true);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
