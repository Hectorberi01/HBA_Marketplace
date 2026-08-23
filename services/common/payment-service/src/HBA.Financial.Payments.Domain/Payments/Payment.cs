using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Domain.Payments.Events;

namespace HBA.Financial.Payments.Domain.Payments;

/// <summary>
/// Paiement d'une commande. Modélise le cycle PSP (Mobile Money, carte…) :
/// initiation → autorisation → encaissement, ou échec / remboursement. Émet les
/// faits que le Saga d'Ordering consomme pour confirmer ou annuler la commande.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>
{
    private readonly List<PaymentRefund> _refunds = new();

    private Payment()
    {
    }

    private Payment(PaymentId id, Guid orderId,
        PaymentOrderType orderType, Guid buyerId, Money amount, PaymentMethod method, string provider, PaymentFlow flow)
        : base(id)
    {
        OrderId = orderId;
        OrderType = orderType;
        BuyerId = buyerId;
        Amount = amount;
        Method = method;
        Provider = provider;
        Flow = flow;
        Status = PaymentStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;

        Raise(new PaymentInitiatedDomainEvent(
            id.Value, orderId, orderType.ToString(), buyerId, amount.Amount, amount.Currency, provider));
    }

    public Guid OrderId { get; private set; }

    /// <summary>Univers de la commande payée — voir <see cref="PaymentOrderType"/>.</summary>
    public PaymentOrderType OrderType { get; private set; }
    public Guid BuyerId { get; private set; }
    public Money Amount { get; private set; } = default!;
    public PaymentMethod Method { get; private set; }
    public string Provider { get; private set; } = default!;
    public PaymentFlow Flow { get; private set; }
    public string? ProviderReference { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CapturedAtUtc { get; private set; }

    /// <summary>
    /// Date de libération de l'escrow. Tant qu'elle est nulle après encaissement,
    /// les fonds sont gelés (modèle marketplace : on ne reverse au vendeur qu'à la
    /// livraison confirmée).
    /// </summary>
    public DateTime? EscrowReleasedAt { get; private set; }

    /// <summary>Vrai si le paiement est encaissé mais l'escrow pas encore libéré.</summary>
    public bool IsEscrowHeld => Status == PaymentStatus.Captured && EscrowReleasedAt is null;

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds
        .Where(r => r.Status == PaymentRefundStatus.Succeeded)
        .Sum(r => r.Amount.Amount);

    public decimal RefundableAmount => Amount.Amount - RefundedAmount;

    public static Result<Payment> Create(Guid orderId, PaymentOrderType orderType, Guid buyerId, Money amount, PaymentMethod method, string provider, PaymentFlow flow)
    {
        if (orderId == Guid.Empty)
        {
            return Error.Validation("payments.order_required", "La commande est obligatoire.");
        }

        if (amount.Amount <= 0m)
        {
            return Error.Validation("payments.amount_invalid", "Le montant à payer doit être positif.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return Error.Validation("payments.provider_required", "Le prestataire de paiement est obligatoire.");
        }

        return new Payment(PaymentId.New(), orderId, orderType, buyerId, amount, method, provider.Trim(), flow);
    }

    /// <summary>
    /// Rattache la référence de session/intention renvoyée par le PSP au moment
    /// de l'initiation (session_id du Checkout ou id du PaymentIntent). Sert de
    /// corrélation pour les webhooks et le retour de redirection.
    /// </summary>
    public Result AttachGatewaySession(string providerReference)
    {
        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "La session PSP ne peut être rattachée qu'à un paiement en attente."));
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            return Result.Failure(Error.Validation("payments.provider_reference_required", "La référence PSP est obligatoire."));
        }

        ProviderReference = providerReference.Trim();
        return Result.Success();
    }

    /// <summary>Autorisation préalable (réservation de fonds), sans encaissement.</summary>
    public Result Authorize(string providerReference)
    {
        if (Status != PaymentStatus.Pending)
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "Le paiement n'est pas en attente d'autorisation."));
        }

        Status = PaymentStatus.Authorized;
        ProviderReference = providerReference;
        return Result.Success();
    }

    /// <summary>Encaissement effectif. Émet PaymentCaptured.</summary>
    public Result Capture(string providerReference)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Authorized))
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "Le paiement ne peut pas être encaissé dans cet état."));
        }

        Status = PaymentStatus.Captured;
        ProviderReference = providerReference;
        CapturedAtUtc = DateTime.UtcNow;
        Raise(new PaymentCapturedDomainEvent(
            Id.Value, OrderId, OrderType.ToString(), Provider, Method.ToString(), Amount.Amount, Amount.Currency));
        return Result.Success();
    }

    /// <summary>Échec du paiement. Émet PaymentFailed.</summary>
    public Result Fail(string reason)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.Refunded)
        {
            return Result.Failure(Error.Conflict("payments.invalid_transition", "Un paiement encaissé ne peut pas échouer."));
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        Raise(new PaymentFailedDomainEvent(
            Id.Value, OrderId, OrderType.ToString(), reason, Provider, Method.ToString(), Amount.Currency));
        return Result.Success();
    }

    /// <summary>Remboursement d'un paiement encaissé. Émet PaymentRefunded.</summary>
    public Result Refund()
    {
        var result = BeginRefund(
            Money.Create(RefundableAmount, Amount.Currency).Value,
            "Remboursement total.",
            $"payment:{Id.Value}:refund:full",
            returnId: null,
            externalRefundId: null,
            nowUtc: DateTime.UtcNow);

        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        MarkRefundSucceeded(result.Value.Id, result.Value.Id.ToString(), DateTime.UtcNow);
        return Result.Success();
    }

    public Result<PaymentRefund> BeginRefund(
        Money amount,
        string reason,
        string idempotencyKey,
        Guid? returnId,
        Guid? externalRefundId,
        DateTime nowUtc)
    {
        if (Status != PaymentStatus.Captured)
        {
            return Error.Conflict("payments.not_refundable", "Seul un paiement encaissé peut être remboursé.");
        }

        if (amount.Amount <= 0m)
        {
            return Error.Validation("payments.refund_amount_invalid", "Le montant a rembourser doit etre positif.");
        }

        if (!string.Equals(amount.Currency, Amount.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("payments.refund_currency_mismatch", "La devise du remboursement ne correspond pas au paiement.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Error.Validation("payments.refund_idempotency_key_required", "La cle d'idempotence du remboursement est obligatoire.");
        }

        var normalizedKey = idempotencyKey.Trim();
        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == normalizedKey);
        if (existing is not null)
        {
            if (existing.Amount.Amount != amount.Amount
                || !string.Equals(existing.Amount.Currency, amount.Currency, StringComparison.OrdinalIgnoreCase)
                || existing.ReturnId != returnId
                || existing.ExternalRefundId != externalRefundId)
            {
                return Error.Conflict(
                    "payments.refund_idempotency_conflict",
                    "La cle d'idempotence existe deja pour une demande differente.");
            }

            if (existing.Status == PaymentRefundStatus.Failed)
            {
                existing.Retry(nowUtc);
            }

            return existing;
        }

        var pendingAmount = _refunds
            .Where(r => r.Status == PaymentRefundStatus.Processing)
            .Sum(r => r.Amount.Amount);

        var available = Amount.Amount - RefundedAmount - pendingAmount;
        if (amount.Amount > available)
        {
            return Error.Conflict("payments.refund_amount_exceeds_remaining", "Le montant depasse le solde remboursable.");
        }

        var refund = new PaymentRefund(
            Guid.NewGuid(),
            Id,
            returnId,
            externalRefundId,
            amount,
            string.IsNullOrWhiteSpace(reason) ? "Remboursement." : reason.Trim(),
            normalizedKey,
            nowUtc);

        _refunds.Add(refund);
        return refund;
    }

    public Result MarkRefundSucceeded(Guid refundId, string providerRefundId, DateTime nowUtc)
    {
        var refund = _refunds.FirstOrDefault(r => r.Id == refundId);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("payments.refund_not_found", "Remboursement introuvable."));
        }

        if (refund.Status == PaymentRefundStatus.Succeeded)
        {
            return Result.Success();
        }

        refund.MarkSucceeded(providerRefundId, nowUtc);
        if (RefundableAmount <= 0m)
        {
            Status = PaymentStatus.Refunded;
        }

        Raise(new PaymentRefundedDomainEvent(
            Id.Value,
            OrderId,
            OrderType.ToString(),
            BuyerId,
            Provider,
            refund.Amount.Amount,
            refund.Amount.Currency,
            refund.Id,
            refund.ReturnId,
            refund.ExternalRefundId,
            refund.IdempotencyKey,
            refund.ProviderRefundId ?? providerRefundId));
        return Result.Success();
    }

    public Result MarkRefundFailed(Guid refundId, string reason, DateTime nowUtc)
    {
        var refund = _refunds.FirstOrDefault(r => r.Id == refundId);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("payments.refund_not_found", "Remboursement introuvable."));
        }

        refund.MarkFailed(reason, nowUtc);
        Raise(new PaymentRefundFailedDomainEvent(
            Id.Value,
            OrderId,
            OrderType.ToString(),
            Provider,
            refund.Amount.Amount,
            refund.Amount.Currency,
            refund.Id,
            refund.ReturnId,
            refund.ExternalRefundId,
            refund.IdempotencyKey,
            refund.FailureReason ?? reason));
        return Result.Success();
    }

    /// <summary>
    /// Libère l'escrow à la livraison confirmée : les fonds encaissés deviennent
    /// reversables au vendeur (le payout est ensuite produit par Settlement).
    /// Idempotent : sans effet si déjà libéré.
    /// </summary>
    public Result ReleaseEscrow()
    {
        if (Status != PaymentStatus.Captured)
        {
            return Result.Failure(Error.Conflict("payments.escrow_not_releasable", "Seul un paiement encaissé peut voir son escrow libéré."));
        }

        if (EscrowReleasedAt is not null)
        {
            return Result.Success();
        }

        EscrowReleasedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
