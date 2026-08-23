using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Commands.ExecuteRefund;

/// <summary>
/// Exécute UN remboursement décidé : appelle le prestataire, puis écrit l'issue.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE COMMANDE N'AVAIT AUCUN ÉMETTEUR. AUCUN REMBOURSEMENT N'ABOUTISSAIT.
///
/// Le gestionnaire était écrit, complet, testable — et personne ne l'appelait.
/// `DecideRefundCommandHandler` écrivait la décision en base, `RefundRetryWorker`
/// se contentait de journaliser « active », et rien, nulle part, ne reliait les
/// deux. `ReturnStatus.Refunded` était INATTEIGNABLE : aucun chemin du code ne
/// menait à `MarkRefundSucceeded`.
///
/// Le raccord est désormais : `DecideRefund` écrit un `Refund` en `Pending` →
/// `RefundRetryWorker` balaie les remboursements en attente → cette commande.
///
/// POURQUOI UN BALAYAGE ET NON UN APPEL DIRECT DEPUIS LA DÉCISION.
///
/// Appeler le prestataire dans la foulée de `DecideRefund` — dans le gestionnaire
/// de domaine, ou juste après — verserait l'argent AVANT que la décision ne soit
/// committée : les gestionnaires de domaine s'exécutent à l'INTÉRIEUR de
/// `SaveChangesAsync` (voir `ModuleDbContext`), avant le `base.SaveChangesAsync`.
/// Un incident entre les deux laisserait un virement parti sans aucune trace en
/// base. Le balayage lit ce qui est COMMITTÉ : il ne peut rien exécuter qui
/// n'existe pas.
///
/// Le prix est un délai — quelques secondes entre la décision et le versement.
/// C'est précisément le délai que `ReturnRefundApprovedIntegrationEvent` sert à
/// expliquer au client.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ExecuteRefundCommandHandler : ICommandHandler<Commands.ExecuteRefundCommand>
{
    /// <summary>
    /// AU-DELÀ, ON ARRÊTE D'ESSAYER ET ON APPELLE UN HUMAIN.
    ///
    /// Un remboursement irréparable — paiement sans référence prestataire, opérateur
    /// qui refuse le canal, montant devenu incohérent — échouerait sinon toutes les
    /// vingt secondes pour toujours, en noyant les journaux et sans que personne
    /// n'apprenne que le client attend.
    /// </summary>
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
            // état depuis lequel un opérateur peut relancer, rejeter ou clore. Sans
            // cette sortie, un client attendrait son argent sans qu'aucune ligne de
            // travail n'existe nulle part.
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

        // Lu AVANT la réservation : si order-service est indisponible, mieux vaut ne
        // rien avoir écrit et laisser le tour suivant réessayer.
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

        // ═════════════════════════════════════════════════════════════════════
        // RÉSERVATION AVANT L'APPEL AU PRESTATAIRE. C'EST LE VERROU.
        //
        // `BeginRefund` passe le remboursement en `Processing` et incrémente
        // `Version`, jeton de concurrence de l'agrégat. Deux exécutants simultanés
        // — un rejeu et le balayage, ou deux répliques — se disputent ce
        // `SaveChanges` : le second lève `DbUpdateConcurrencyException` et n'atteint
        // JAMAIS l'appel au prestataire.
        //
        // L'exception n'est pas rattrapée ici : la couche Application ne référence
        // pas EF Core, et surtout un conflit de concurrence n'est pas une erreur
        // métier. Elle remonte au `RefundRetryWorker`, qui la journalise et reprend
        // au tour suivant — où le remboursement sera vu `Processing`, donc déjà
        // pris en charge.
        // ═════════════════════════════════════════════════════════════════════
        var reservation = request.BeginRefund(refund.Id, _clock.UtcNow);
        if (reservation.IsFailure)
        {
            return reservation;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LA CLÉ TRANSMISE EST CELLE DU `Refund`, DÉTERMINISTE PAR CONSTRUCTION :
        // `return:{ReturnId}:refund:{n}`. C'est elle qui rend l'appel rejouable —
        // payment-service reconnaît une tentative déjà aboutie et rend son issue au
        // lieu de verser une seconde fois. Une clé tirée au hasard à chaque tentative
        // n'empêcherait rien du tout.
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
        // gestionnaire de domaine traduit en `ReturnRefundedIntegrationEvent`. Cet
        // événement part dans l'outbox DANS LE MÊME `SaveChangesAsync` que le
        // passage en `Refunded` : wallet-service ne peut pas contre-passer un gain
        // pour un remboursement que notre base n'aurait pas enregistré, ni l'inverse.
        var marked = request.MarkRefundSucceeded(refund.Id, payment.Value.ProviderRefundId, _clock.UtcNow);
        if (marked.IsFailure)
        {
            return marked;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
