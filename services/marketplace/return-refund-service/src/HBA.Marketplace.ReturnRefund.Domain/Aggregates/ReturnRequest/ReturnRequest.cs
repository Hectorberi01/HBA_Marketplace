using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Events;
using HBA.Marketplace.ReturnRefund.Domain.Policies;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using Money = HBA.Marketplace.ReturnRefund.Domain.ValueObjects.Money;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class ReturnRequest : AggregateRoot<Guid>
{
    private readonly List<ReturnItem> _items = new();
    private readonly List<ReturnEvidence> _evidence = new();
    private readonly List<ReturnShipment> _shipments = new();
    private readonly List<ReturnInspection> _inspections = new();
    private readonly List<Refund> _refunds = new();
    private readonly List<ReturnStatusHistory> _history = new();

    private ReturnRequest()
    {
    }

    private ReturnRequest(
        Guid id,
        string returnNumber,
        Guid orderId,
        Guid? sellerOrderId,
        Guid customerId,
        Guid sellerId,
        Guid storeId,
        ReturnResolution resolutionRequested,
        ReturnReasonCode reasonCode,
        string? customerComment,
        Money estimatedRefundAmount,
        PolicySnapshot policySnapshot,
        DateTime createdAtUtc)
        : base(id)
    {
        ReturnNumber = returnNumber;
        OrderId = orderId;
        SellerOrderId = sellerOrderId;
        CustomerId = customerId;
        SellerId = sellerId;
        StoreId = storeId;
        Status = ReturnStatus.Requested;
        ResolutionRequested = resolutionRequested;
        ReasonCode = reasonCode;
        CustomerComment = customerComment;
        Currency = estimatedRefundAmount.Currency;
        EstimatedRefundAmount = estimatedRefundAmount.Amount;
        ReturnShippingPayer = ReturnShippingPolicy.PayerFor(reasonCode, policySnapshot);
        PolicySnapshot = policySnapshot;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc.Date.AddDays(policySnapshot.ReturnWindowDays).AddDays(1).AddTicks(-1);
        Version = 1;

        AddHistory(Status, "Demande creee.", createdAtUtc, null);
        Raise(new ReturnRequestedDomainEvent(id, orderId, customerId, sellerId));
    }

    public string ReturnNumber { get; private set; } = string.Empty;
    public Guid OrderId { get; private set; }
    public Guid? SellerOrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid StoreId { get; private set; }
    public ReturnStatus Status { get; private set; }
    public ReturnResolution ResolutionRequested { get; private set; }
    public ReturnReasonCode ReasonCode { get; private set; }
    public string? CustomerComment { get; private set; }
    public string Currency { get; private set; } = "XOF";
    public decimal EstimatedRefundAmount { get; private set; }
    public decimal? ApprovedRefundAmount { get; private set; }
    public string ReturnShippingPayer { get; private set; } = "SELLER";
    public PolicySnapshot PolicySnapshot { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyCollection<ReturnItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<ReturnEvidence> Evidence => _evidence.AsReadOnly();
    public IReadOnlyCollection<ReturnShipment> Shipments => _shipments.AsReadOnly();
    public IReadOnlyCollection<ReturnInspection> Inspections => _inspections.AsReadOnly();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();
    public IReadOnlyCollection<ReturnStatusHistory> History => _history.AsReadOnly();

    public static Result<ReturnRequest> Create(
        string returnNumber,
        Guid orderId,
        Guid? sellerOrderId,
        Guid customerId,
        Guid sellerId,
        Guid storeId,
        ReturnResolution resolutionRequested,
        ReturnReasonCode reasonCode,
        string? customerComment,
        Money estimatedRefundAmount,
        PolicySnapshot policySnapshot,
        IEnumerable<ReturnItemDraft> items,
        DateTime deliveredAtUtc,
        DateTime nowUtc)
    {
        if (orderId == Guid.Empty || customerId == Guid.Empty || sellerId == Guid.Empty || storeId == Guid.Empty)
        {
            return Error.Validation("return.identity_required", "La commande, le client, le vendeur et le store sont obligatoires.");
        }

        if (string.IsNullOrWhiteSpace(returnNumber))
        {
            return Error.Validation("return.number_required", "Le numero de retour est obligatoire.");
        }

        var eligibility = ReturnEligibilityPolicy.Evaluate(policySnapshot, resolutionRequested, deliveredAtUtc, nowUtc);
        if (eligibility.IsFailure)
        {
            return Result.Failure<ReturnRequest>(eligibility.Error);
        }

        var requestedItems = items.ToList();
        if (requestedItems.Count == 0)
        {
            return Error.Validation("return.items_required", "Au moins une ligne doit etre retournee ou remboursee.");
        }

        var request = new ReturnRequest(
            Guid.NewGuid(),
            returnNumber.Trim(),
            orderId,
            sellerOrderId,
            customerId,
            sellerId,
            storeId,
            resolutionRequested,
            reasonCode,
            customerComment,
            estimatedRefundAmount,
            policySnapshot,
            nowUtc);

        foreach (var item in requestedItems)
        {
            var line = ReturnItem.Create(item);
            if (line.IsFailure)
            {
                return Result.Failure<ReturnRequest>(line.Error);
            }

            request._items.Add(line.Value);
        }

        request.MoveTo(policySnapshot.AutoApproveReasons.Contains(reasonCode)
            ? ReturnStatus.Approved
            : ReturnStatus.AwaitingApproval, "Eligibilite validee.", nowUtc, null);

        if (request.Status == ReturnStatus.Approved)
        {
            request.Raise(new ReturnApprovedDomainEvent(request.Id, orderId, sellerId));
        }

        return request;
    }

    public Result AddEvidence(string mediaId, string kind, string? caption, DateTime nowUtc)
    {
        if (Status is ReturnStatus.Cancelled or ReturnStatus.Closed or ReturnStatus.Refunded)
        {
            return Result.Failure(Error.Conflict("return.evidence_for_closed", "Impossible d'ajouter une preuve sur un dossier ferme."));
        }

        if (string.IsNullOrWhiteSpace(mediaId))
        {
            return Result.Failure(Error.Validation("return.evidence.media_required", "La reference media est obligatoire."));
        }

        _evidence.Add(new ReturnEvidence(Guid.NewGuid(), Id, mediaId.Trim(), kind.Trim(), caption, nowUtc));
        Touch();
        return Result.Success();
    }

    public Result Approve(DateTime nowUtc, Guid? actorId)
    {
        var transition = MoveTo(ReturnStatus.Approved, "Retour approuve.", nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        Raise(new ReturnApprovedDomainEvent(Id, OrderId, SellerId));
        return Result.Success();
    }

    public Result Reject(string reason, DateTime nowUtc, Guid? actorId)
    {
        var transition = MoveTo(ReturnStatus.Rejected, reason, nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        ResolvedAtUtc = nowUtc;
        Raise(new ReturnRejectedDomainEvent(Id, OrderId, reason));
        return Result.Success();
    }

    public Result Cancel(DateTime nowUtc, Guid? actorId)
    {
        var transition = MoveTo(ReturnStatus.Cancelled, "Demande annulee.", nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        ResolvedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Le délai de retour est passé et le dossier n'a pas avancé : on le ferme.</summary>
    public Result Expire(DateTime nowUtc)
    {
        var transition = MoveTo(ReturnStatus.Expired, "Delai de retour depasse.", nowUtc, null);
        if (transition.IsFailure)
        {
            return transition;
        }

        ResolvedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result RegisterShipment(string deliveryId, string mode, string? trackingNumber, DateTime nowUtc, Guid? actorId)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            return Result.Failure(Error.Validation("return.shipment.delivery_required", "La reference delivery est obligatoire."));
        }

        var transition = MoveTo(ReturnStatus.AwaitingReturn, "Instruction de retour generee.", nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        _shipments.Add(new ReturnShipment(Guid.NewGuid(), Id, deliveryId.Trim(), mode.Trim(), trackingNumber, nowUtc));
        Raise(new ReturnShipmentRegisteredDomainEvent(Id, deliveryId.Trim()));
        return Result.Success();
    }

    public Result MarkInTransit(DateTime nowUtc)
        => MoveTo(ReturnStatus.InReturnTransit, "Retour pris en charge.", nowUtc, null);

    public Result Receive(DateTime nowUtc, Guid? actorId)
    {
        var transition = MoveTo(ReturnStatus.Received, "Retour recu.", nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        foreach (var item in _items)
        {
            item.MarkReceived(item.RequestedQuantity);
        }

        Raise(new ReturnReceivedDomainEvent(Id, nowUtc));
        return Result.Success();
    }

    public Result Inspect(
        InspectionCondition condition,
        StockDisposition disposition,
        string notes,
        DateTime nowUtc,
        Guid? actorId)
    {
        var transition = MoveTo(ReturnStatus.InspectionPending, "Inspection demarree.", nowUtc, actorId);
        if (transition.IsFailure && Status != ReturnStatus.InspectionPending)
        {
            return transition;
        }

        _inspections.Add(new ReturnInspection(Guid.NewGuid(), Id, condition, disposition, notes, nowUtc, actorId));
        Raise(new ReturnInspectedDomainEvent(Id, condition, disposition));
        return Result.Success();
    }

    /// <summary>Fixe le montant rendu au client et ouvre le remboursement.</summary>
    /// <param name="refundableLines">Lignes remboursables reconstituées depuis la commande.</param>
    /// <param name="capturedRemainingCeiling">
    /// `CapturedAmount − AlreadyRefundedAmount` d'order-service : ce que la
    /// commande peut encore rendre, tous dossiers confondus.
    /// </param>
    public Result DecideRefund(
        Money amount,
        IReadOnlyCollection<RefundableLine> refundableLines,
        decimal capturedRemainingCeiling,
        DateTime nowUtc,
        Guid? actorId)
    {
        var engage = TotalRefunded();
        var breakdown = RefundCalculationPolicy.Compute(
            refundableLines,
            PolicySnapshot,
            new Money(engage, Currency));

        // `EngagementNonAbouti()` ET NON `engage` — SINON ON COMPTE DEUX FOIS.
        var calculation = RefundCalculationPolicy.Validate(
            amount, breakdown, capturedRemainingCeiling, EngagementNonAbouti());
        if (calculation.IsFailure)
        {
            return calculation;
        }

        var transition = MoveTo(ReturnStatus.RefundPending, "Remboursement decide.", nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        ApprovedRefundAmount = amount.Amount;

        // La clé est DÉTERMINISTE — `return:{Id}:refund:{n}` — et c'est ce qui la
        // rend utilisable comme garde : un rejeu de la même décision refabrique la
        // même chaîne et se heurte à l'index unique.
        var refund = Refund.Create(Id, amount, breakdown, $"return:{Id}:refund:{_refunds.Count + 1}", nowUtc);
        _refunds.Add(refund);

        Raise(new RefundRequestedDomainEvent(Id, refund.Id, OrderId, CustomerId, SellerId, amount.Amount, amount.Currency));
        return Result.Success();
    }

    /// <summary>Réserve un remboursement avant d'appeler le prestataire.</summary>
    public Result BeginRefund(Guid refundId, DateTime nowUtc)
    {
        var refund = _refunds.FirstOrDefault(r => r.Id == refundId);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("refund.not_found", "Remboursement introuvable."));
        }

        if (refund.Status is RefundStatus.Succeeded or RefundStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict("refund.already_settled", "Ce remboursement est deja clos."));
        }

        refund.MarkProcessing(nowUtc);
        Touch();
        return Result.Success();
    }

    public Result MarkRefundFailed(Guid refundId, string reason, DateTime nowUtc)
    {
        var refund = _refunds.FirstOrDefault(r => r.Id == refundId);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("refund.not_found", "Remboursement introuvable."));
        }

        // Le dossier RESTE en `RefundPending` : l'échec porte sur la tentative, pas
        // sur la décision.
        refund.MarkFailed(reason, nowUtc);
        Touch();
        return Result.Success();
    }

    /// <summary>Le versement a échoué trop de fois : on sort de l'automatique.</summary>
    public Result EscalateToManualReview(string reason, DateTime nowUtc)
        => MoveTo(ReturnStatus.ManualReview, reason, nowUtc, null);

    public Result MarkRefundSucceeded(Guid refundId, string providerRefundId, DateTime nowUtc)
    {
        var refund = _refunds.FirstOrDefault(r => r.Id == refundId);
        if (refund is null)
        {
            return Result.Failure(Error.NotFound("refund.not_found", "Remboursement introuvable."));
        }

        refund.MarkSucceeded(providerRefundId, nowUtc);
        var transition = MoveTo(ReturnStatus.Refunded, "Remboursement execute.", nowUtc, null);
        if (transition.IsFailure)
        {
            return transition;
        }

        ResolvedAtUtc = nowUtc;

        // C'EST CET ÉVÉNEMENT QUI FAIT EXISTER LE REMBOURSEMENT AILLEURS.
        Raise(new RefundSucceededDomainEvent(
            Id, refundId, OrderId, CustomerId, SellerId, refund.Amount, refund.Currency, providerRefundId,
            LignesReprises(), TotalRefunded()));

        return Result.Success();
    }

    public Result Close(DateTime nowUtc, Guid? actorId)
    {
        var transition = MoveTo(ReturnStatus.Closed, "Dossier cloture.", nowUtc, actorId);
        if (transition.IsFailure)
        {
            return transition;
        }

        ResolvedAtUtc ??= nowUtc;
        Raise(new ReturnClosedDomainEvent(Id, Status));
        return Result.Success();
    }

    /// <summary>Ce que ce dossier a décidé de rembourser SANS que l'argent soit parti.</summary>
    public decimal EngagementNonAbouti()
        => _refunds
            .Where(r => r.Status is RefundStatus.Pending or RefundStatus.Processing)
            .Sum(r => r.Amount);

    /// <summary>
    /// Les lignes reprises par ce dossier, quantité cumulée, telles qu'elles
    /// partent vers order-service.
    /// </summary>
    private IReadOnlyCollection<RefundedLineSnapshot> LignesReprises()
        => _items
            .Select(i => new RefundedLineSnapshot(
                i.OrderItemId,
                i.ReceivedQuantity > 0 ? i.ReceivedQuantity : i.RequestedQuantity))
            .Where(l => l.Quantity > 0)
            .ToList();

    /// <summary>Ce que ce dossier a déjà ENGAGÉ en remboursements.</summary>
    public decimal TotalRefunded()
        => _refunds
            .Where(r => r.Status is RefundStatus.Pending
                or RefundStatus.Processing
                or RefundStatus.Succeeded
                or RefundStatus.PartiallySucceeded)
            .Sum(r => r.Amount);

    private Result MoveTo(ReturnStatus status, string reason, DateTime nowUtc, Guid? actorId)
    {
        if (!ReturnStateMachine.CanTransition(Status, status))
        {
            return Result.Failure(Error.Conflict("return.transition_invalid", $"Transition {Status} -> {status} invalide."));
        }

        Status = status;
        AddHistory(status, reason, nowUtc, actorId);
        Touch();
        return Result.Success();
    }

    private void AddHistory(ReturnStatus status, string reason, DateTime nowUtc, Guid? actorId)
        => _history.Add(new ReturnStatusHistory(Guid.NewGuid(), Id, status, reason, nowUtc, actorId));

    private void Touch() => Version++;
}

public sealed record ReturnItemDraft(
    Guid OrderItemId,
    Guid ProductId,
    Guid? VariantId,
    string SkuSnapshot,
    string NameSnapshot,
    int OrderedQuantity,
    int DeliveredQuantity,
    int AlreadyReturnedQuantity,
    int RequestedQuantity,
    Money UnitPaidAmount,
    ReturnReasonCode ReasonCode,
    InspectionCondition ConditionDeclared);
