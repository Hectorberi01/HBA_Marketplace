using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;

namespace HBA.Orders.Application.Orders.Commands;

/// <summary>
/// Inscrit sur la commande ce qu'un dossier de retour lui a définitivement retiré :
/// l'argent rendu, et les exemplaires repris ligne à ligne.
/// </summary>
public sealed record RecordReturnSettlementCommand(
    Guid OrderId,
    Guid ReturnRequestId,
    decimal TotalRefundedAmount,
    IReadOnlyCollection<ReturnSettlementLineDraft> Lines) : ICommand;

internal sealed class RecordReturnSettlementCommandHandler : ICommandHandler<RecordReturnSettlementCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public RecordReturnSettlementCommandHandler(IOrderRepository orderRepository, IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RecordReturnSettlementCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var enregistre = order.RecordReturnSettlement(
            command.ReturnRequestId,
            command.TotalRefundedAmount,
            command.Lines,
            DateTime.UtcNow);

        if (enregistre.IsFailure)
        {
            return enregistre;
        }

        // TOUJOURS, MÊME QUAND RIEN N'A BOUGÉ.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
