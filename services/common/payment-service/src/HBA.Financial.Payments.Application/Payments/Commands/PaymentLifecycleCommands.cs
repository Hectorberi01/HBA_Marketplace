using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Application.Abstractions.Gateways;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Financial.Wallet.Contracts;

namespace HBA.Financial.Payments.Application.Payments.Commands;

/// <summary>Encaisse un paiement (simule la confirmation du PSP).</summary>
public sealed record CapturePaymentCommand(Guid PaymentId, string ProviderReference) : ICommand;

/// <summary>Marque un paiement en échec.</summary>
public sealed record FailPaymentCommand(Guid PaymentId, string Reason) : ICommand;

/// <summary>Rembourse un paiement encaissé.</summary>
public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal? Amount = null,
    string? Currency = null,
    string? Reason = null,
    string? IdempotencyKey = null,
    Guid? ReturnId = null,
    Guid? ExternalRefundId = null) : ICommand<RefundPaymentResult>;

public sealed record RefundPaymentResult(
    Guid PaymentId,
    Guid RefundId,
    string ProviderRefundId,
    string Status,
    decimal Amount,
    string Currency);

internal sealed class CapturePaymentCommandHandler : ICommandHandler<CapturePaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public CapturePaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CapturePaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Capture(command.ProviderReference);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class FailPaymentCommandHandler : ICommandHandler<FailPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public FailPaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(FailPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Fail(string.IsNullOrWhiteSpace(command.Reason) ? "Paiement refusé." : command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Rembourse un paiement encaissé — chez le prestataire, puis en base.</summary>
internal sealed class RefundPaymentCommandHandler : ICommandHandler<RefundPaymentCommand, RefundPaymentResult>
{
    /// <summary>Motif inscrit au grand livre du portefeuille.</summary>
    private const string MotifPortefeuille = "payment_refund";

    private readonly IPaymentRepository _repository;
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly ICustomerWalletApi _customerWallet;
    private readonly IPaymentsUnitOfWork _unitOfWork;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;

    public RefundPaymentCommandHandler(
        IPaymentRepository repository,
        IPaymentGatewayResolver gatewayResolver,
        ICustomerWalletApi customerWallet,
        IPaymentsUnitOfWork unitOfWork,
        ILogger<RefundPaymentCommandHandler> logger)
    {
        _repository = repository;
        _gatewayResolver = gatewayResolver;
        _customerWallet = customerWallet;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RefundPaymentResult>> Handle(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Error.NotFound("payments.not_found", "Paiement introuvable.");
        }

        // ── 1. L'état permet-il un remboursement ? ────────────────────────────
        if (payment.Status != PaymentStatus.Captured)
        {
            var existing = payment.Refunds.FirstOrDefault(r =>
                r.IdempotencyKey == (command.IdempotencyKey ?? $"payment:{command.PaymentId}:refund:full"));

            if (existing?.Status == PaymentRefundStatus.Succeeded)
            {
                return ToResult(payment, existing);
            }

            return Error.Conflict("payments.not_refundable", "Seul un paiement encaissé peut être remboursé.");
        }

        // Sans référence du prestataire, il n'y a rien à rembourser CHEZ LUI : le
        // paiement n'a jamais abouti de son côté.
        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
        {
            return Error.Conflict(
                "payments.no_provider_reference",
                "Ce paiement ne porte aucune référence de prestataire : il ne peut pas être remboursé.");
        }

        var amount = Money.Create(command.Amount ?? payment.RefundableAmount, command.Currency ?? payment.Amount.Currency);
        if (amount.IsFailure)
        {
            return amount.Error;
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? $"payment:{payment.Id.Value}:refund:full"
            : command.IdempotencyKey.Trim();

        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund?.Status is PaymentRefundStatus.Succeeded or PaymentRefundStatus.Processing)
        {
            return ToResult(payment, existingRefund);
        }

        var gateway = _gatewayResolver.Resolve(payment.Provider);
        if (gateway.IsFailure)
        {
            return gateway.Error;
        }

        var begin = payment.BeginRefund(
            amount.Value,
            command.Reason ?? "Remboursement.",
            idempotencyKey,
            command.ReturnId,
            command.ExternalRefundId,
            DateTime.UtcNow);

        if (begin.IsFailure)
        {
            return begin.Error;
        }

        var refundRequest = begin.Value;
        if (refundRequest.Status == PaymentRefundStatus.Succeeded)
        {
            return ToResult(payment, refundRequest);
        }

        if (refundRequest.Status == PaymentRefundStatus.Processing)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // 2. L'ARGENT REPART — PAR LE PRESTATAIRE, OU PAR LE PORTEFEUILLE (D33).
        var refund = gateway.Value.SupportsRefund
            ? await gateway.Value.RefundAsync(
                new GatewayRefundContext(
                    payment.ProviderReference,
                    refundRequest.Amount.Amount,
                    refundRequest.Amount.Currency,
                    refundRequest.Reason,
                    refundRequest.IdempotencyKey),
                cancellationToken)
            : await CrediterLePortefeuilleAsync(payment, refundRequest, cancellationToken);

        // PANNE PASSAGÈRE : ON NE DÉCIDE RIEN, ON LAISSE REJOUER.
        if (!refund.Success && refund.Transient)
        {
            _logger.LogCritical(
                "Remboursement INTERROMPU chez {Provider} pour le paiement {PaymentId} "
                + "(référence {ProviderReference}) : {Erreur}. Le prestataire n'a pas répondu — "
                + "on ignore si l'argent est parti. La demande {RefundId} reste « en cours ».",
                payment.Provider, payment.Id.Value, payment.ProviderReference, refund.Error, refundRequest.Id);

            return Error.DependencyUnavailable(
                "payments.refund_gateway_unavailable",
                refund.Error ?? "Le prestataire de paiement n'a pas répondu à la demande de remboursement.");
        }

        // REFUS MÉTIER : C'EST DÉFINITIF, ON L'ENREGISTRE ET ON REND LA MAIN.
        if (!refund.Success)
        {
            payment.MarkRefundFailed(refundRequest.Id, refund.Error ?? "Remboursement refuse par le prestataire.", DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogCritical(
                "Remboursement REFUSÉ par {Provider} pour le paiement {PaymentId} "
                + "(référence {ProviderReference}) : {Erreur}. Ce prestataire SAIT rembourser — "
                + "le refus porte donc sur cette demande précise, pas sur sa capacité. Le paiement "
                + "reste encaissé : le client N'EST PAS remboursé, arbitrage manuel requis.",
                payment.Provider, payment.Id.Value, payment.ProviderReference, refund.Error);

            return Error.BusinessRule(
                "payments.refund_rejected",
                refund.Error ?? "Le prestataire a refusé le remboursement.");
        }

        // ── 3. Et seulement maintenant, la base ───────────────────────────────
        var result = payment.MarkRefundSucceeded(
            refundRequest.Id,
            refund.ProviderReference ?? refundRequest.Id.ToString(),
            DateTime.UtcNow);
        if (result.IsFailure)
        {
            // Ne devrait pas arriver : l'état a été vérifié plus haut.
            _logger.LogCritical(
                "ARGENT REMBOURSÉ CHEZ {Provider} MAIS PAS ENREGISTRÉ — paiement {PaymentId}, "
                + "référence de remboursement {RefundReference}. {Code} : {Message}.",
                payment.Provider, payment.Id.Value, refund.ProviderReference,
                result.Error.Code, result.Error.Message);

            return result.Error;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // La référence porte le chemin : « wallet:… » si l'argent est reparti sur
        // le portefeuille du client, l'identifiant du prestataire sinon.
        _logger.LogInformation(
            "Paiement {PaymentId} remboursé ({Provider}) — référence de remboursement {RefundReference}.",
            payment.Id.Value, payment.Provider, refund.ProviderReference);

        return ToResult(payment, refundRequest);
    }

    /// <summary>
    /// Rend l'argent sur le portefeuille du client, faute de pouvoir le rendre chez
    /// le prestataire (D33).
    /// </summary>
    private async Task<GatewayRefundResult> CrediterLePortefeuilleAsync(
        Payment payment, PaymentRefund refundRequest, CancellationToken cancellationToken)
    {
        var credit = await _customerWallet.CreditRefundAsync(
            payment.BuyerId,
            refundRequest.Amount.Amount,
            refundRequest.Amount.Currency,
            MotifPortefeuille,
            refundRequest.IdempotencyKey,
            cancellationToken);

        if (credit.IsFailure)
        {
            _logger.LogCritical(
                "REMBOURSEMENT IMPOSSIBLE : {Provider} ne sait pas rembourser et le portefeuille du "
                + "client {BuyerId} a refusé le crédit de {Montant} {Devise} pour le paiement "
                + "{PaymentId} — {Code} : {Message}. Le client N'EST PAS remboursé ; la demande "
                + "{RefundId} reste « en cours » et sera rejouée.",
                payment.Provider, payment.BuyerId, refundRequest.Amount.Amount,
                refundRequest.Amount.Currency, payment.Id.Value,
                credit.Error.Code, credit.Error.Message, refundRequest.Id);

            return new GatewayRefundResult(
                Success: false,
                ProviderReference: null,
                Error: $"{credit.Error.Code} : {credit.Error.Message}",
                Transient: true);
        }

        // Un rejeu reconnu est un succès, mais il n'a rien crédité de nouveau.
        if (credit.Value.AlreadyApplied)
        {
            _logger.LogInformation(
                "Crédit de remboursement DÉJÀ APPLIQUÉ au portefeuille du client {BuyerId} "
                + "(écriture {TransactionId}) : rejeu reconnu, rien n'a été crédité de plus.",
                payment.BuyerId, credit.Value.TransactionId);
        }
        else
        {
            _logger.LogInformation(
                "Le prestataire {Provider} ne rembourse pas : {Montant} {Devise} rendus sur le "
                + "portefeuille du client {BuyerId} (écriture {TransactionId}, nouveau solde "
                + "{Solde}). Le virement Mobile Money se fera sur sa demande.",
                payment.Provider, refundRequest.Amount.Amount, refundRequest.Amount.Currency,
                payment.BuyerId, credit.Value.TransactionId, credit.Value.NewBalance);
        }

        // La référence dit PAR QUEL CHEMIN l'argent est reparti.
        return new GatewayRefundResult(
            Success: true,
            ProviderReference: $"wallet:{credit.Value.TransactionId}",
            Error: null);
    }

    private static RefundPaymentResult ToResult(Payment payment, PaymentRefund refund)
        => new(
            payment.Id.Value,
            refund.Id,
            refund.ProviderRefundId ?? refund.Id.ToString(),
            refund.Status.ToString(),
            refund.Amount.Amount,
            refund.Amount.Currency);
}
