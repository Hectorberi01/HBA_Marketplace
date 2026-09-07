using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Commands.ExecuteRefund;

/// <summary>Exécute UN remboursement décidé : appelle le prestataire, puis écrit l'issue.</summary>
internal sealed class ExecuteRefundCommandHandler : ICommandHandler<Commands.ExecuteRefundCommand>
{
    /// <summary>AU-DELÀ, ON ARRÊTE D'ESSAYER ET ON APPELLE UN HUMAIN.</summary>
    private const int MaxTentatives = 5;

    private readonly IReturnRequestRepository _returns;
    private readonly IOrderGrpcClient _orders;
    private readonly IPaymentGrpcClient _payments;
    private readonly IReturnRefundUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ExecuteRefundCommandHandler(
        IReturnRequestRepository returns,
        IOrderGrpcClient orders,
        IPaymentGrpcClient payments,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock)
    {
        _returns = returns;
        _orders = orders;
        _payments = payments;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(Commands.ExecuteRefundCommand command, CancellationToken cancellationToken)
    {
        var request = await _returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null)
        {
            return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        }

        var refund = request.Refunds.FirstOrDefault(r => r.Id == command.RefundId);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("refund.not_found", "Remboursement introuvable."));
        }

        // Déjà versé, ou décision annulée : il n'y a plus rien à faire, et le dire
        // en succès évite que le balayage ne reprenne éternellement la même ligne.
        if (refund.Status is RefundStatus.Succeeded or RefundStatus.Cancelled)
        {
            return Result.Success();
        }

        var tentativesEchouees = refund.Attempts.Count(a => a.Status == RefundStatus.Failed);
        if (tentativesEchouees >= MaxTentatives)
        {
            // ON N'ABANDONNE PAS EN SILENCE. Le dossier passe en `ManualReview`,
            // état depuis lequel un opérateur peut relancer, rejeter ou clore.
            var escalade = request.EscalateToManualReview(
                $"Remboursement non abouti apres {tentativesEchouees} tentatives : arbitrage requis.",
                _clock.UtcNow);

            if (escalade.IsSuccess)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Failure(Error.Conflict(
                "refund.max_attempts_reached",
                "Le remboursement a echoue trop de fois : le dossier passe en arbitrage manuel."));
        }

        // Lu AVANT la réservation : si order-service est indisponible, mieux vaut
        // ne rien avoir écrit et laisser le tour suivant réessayer.
        var order = await _orders.GetOrderReturnContextAsync(request.OrderId, cancellationToken);
        if (order.IsFailure)
        {
            return Result.Failure(order.Error);
        }

        var money = Money.Create(refund.Amount, refund.Currency);
        if (money.IsFailure)
        {
            return Result.Failure(money.Error);
        }

        // RÉSERVATION AVANT L'APPEL AU PRESTATAIRE. C'EST LE VERROU.
        var reservation = request.BeginRefund(refund.Id, _clock.UtcNow);
        if (reservation.IsFailure)
        {
            return reservation;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LA CLÉ TRANSMISE EST CELLE DU `Refund`, DÉTERMINISTE PAR CONSTRUCTION :
        // `return:{ReturnId}:refund:{n}`.
        var payment = await _payments.RefundPaymentAsync(
            order.Value.PaymentId,
            request.Id,
            refund.Id,
            money.Value,
            request.ReasonCode.ToString(),
            refund.IdempotencyKey,
            cancellationToken);

        if (payment.IsFailure)
        {
            request.MarkRefundFailed(refund.Id, payment.Error.Code, _clock.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(payment.Error);
        }

        // `MarkRefundSucceeded` lève `RefundSucceededDomainEvent`, que le
        // gestionnaire de domaine traduit en `ReturnRefundedIntegrationEvent`.
        var marked = request.MarkRefundSucceeded(refund.Id, payment.Value.ProviderRefundId, _clock.UtcNow);
        if (marked.IsFailure)
        {
            return marked;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
