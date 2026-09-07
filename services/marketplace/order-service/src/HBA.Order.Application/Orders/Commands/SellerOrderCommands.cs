using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Application.Orders.Commands;

// LES CINQ GESTES QUE `ORDER_MANAGER` ATTENDAIT (ISSUE-026).

/// <summary>Le vendeur s'engage à honorer sa part.</summary>
public sealed record ConfirmSellerOrderCommand(Guid OrderId, Guid SellerId) : ICommand;

/// <summary>Le vendeur refuse sa part avant de s'être engagé.</summary>
public sealed record RejectSellerOrderCommand(Guid OrderId, Guid SellerId, string Reason) : ICommand;

/// <summary>Le colis se monte. Permission `ORDER_MARK_PREPARING`.</summary>
public sealed record MarkSellerOrderPreparingCommand(Guid OrderId, Guid SellerId) : ICommand;

/// <summary>Le colis attend le livreur.</summary>
public sealed record MarkSellerOrderReadyCommand(Guid OrderId, Guid SellerId) : ICommand;

/// <summary>Le vendeur se dédit après s'être engagé.</summary>
public sealed record CancelSellerOrderCommand(Guid OrderId, Guid SellerId, string Reason) : ICommand;

/// <summary>Les cinq transitions de la part vendeur.</summary>
internal sealed class SellerOrderCommandHandler
    : ICommandHandler<ConfirmSellerOrderCommand>,
      ICommandHandler<RejectSellerOrderCommand>,
      ICommandHandler<MarkSellerOrderPreparingCommand>,
      ICommandHandler<MarkSellerOrderReadyCommand>,
      ICommandHandler<CancelSellerOrderCommand>
{
    private readonly ISellerOrderRepository _sellerOrders;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public SellerOrderCommandHandler(ISellerOrderRepository sellerOrders, IOrderingUnitOfWork unitOfWork)
    {
        _sellerOrders = sellerOrders;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(ConfirmSellerOrderCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.Confirm(maintenant),
            cancellationToken);

    public Task<Result> Handle(RejectSellerOrderCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            // AUCUN MOTIF PAR DÉFAUT, CONTRAIREMENT À L'ARBITRAGE.
            (part, maintenant) => part.Reject(command.Reason, maintenant),
            cancellationToken);

    public Task<Result> Handle(MarkSellerOrderPreparingCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.MarkPreparing(maintenant),
            cancellationToken);

    public Task<Result> Handle(MarkSellerOrderReadyCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.MarkReadyForPickup(maintenant),
            cancellationToken);

    public Task<Result> Handle(CancelSellerOrderCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.OrderId, command.SellerId,
            (part, maintenant) => part.Cancel(command.Reason, maintenant),
            cancellationToken);

    private async Task<Result> MuterAsync(
        Guid orderId,
        Guid sellerId,
        Func<SellerOrder, DateTime, Result> transition,
        CancellationToken cancellationToken)
    {
        var part = await _sellerOrders.FindAsync(orderId, sellerId, cancellationToken);

        // « INTROUVABLE » COUVRE TROIS CAS BIEN DIFFÉRENTS, ET C'EST VOULU.
        if (part is null)
        {
            return Result.Failure(Error.NotFound(
                "ordering.seller_order.not_found", "Commande vendeur introuvable."));
        }

        var resultat = transition(part, DateTime.UtcNow);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
