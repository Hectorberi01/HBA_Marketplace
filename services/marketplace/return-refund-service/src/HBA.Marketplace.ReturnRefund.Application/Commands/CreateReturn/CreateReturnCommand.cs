using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Commands.CreateReturn;

/// <summary>Ouverture d'un dossier de retour par le client.</summary>
public sealed record CreateReturnCommand(
    CreateReturnRequestDto Request,
    Guid? RequestedByUserId) : ICommand<ReturnCreatedDto>;

internal sealed class CreateReturnCommandHandler : ICommandHandler<CreateReturnCommand, ReturnCreatedDto>
{
    private readonly IReturnRequestRepository _returns;
    private readonly IReturnPolicyRepository _policies;
    private readonly IOrderGrpcClient _orders;
    private readonly IReturnRefundUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateReturnCommandHandler(
        IReturnRequestRepository returns,
        IReturnPolicyRepository policies,
        IOrderGrpcClient orders,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock)
    {
        _returns = returns;
        _policies = policies;
        _orders = orders;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ReturnCreatedDto>> Handle(CreateReturnCommand command, CancellationToken cancellationToken)
    {
        if (command.RequestedByUserId is not { } demandeur)
        {
            return Error.Unauthorized("return.identity_required", "Identite de l'appelant requise.");
        }

        if (string.IsNullOrWhiteSpace(command.Request.IdempotencyKey))
        {
            return Error.Validation("return.idempotency_required", "La cle d'idempotence est obligatoire.");
        }

        var existing = await _returns.GetByIdempotencyKeyAsync(command.Request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            // LA REPRISE PAR CLÉ D'IDEMPOTENCE EST AUSSI UNE LECTURE.
            if (existing.CustomerId != demandeur)
            {
                return Error.Validation(
                    "return.idempotency_conflict",
                    "Cette cle d'idempotence ne correspond pas a une demande de ce compte.");
            }

            return ToCreated(existing);
        }

        var context = await _orders.GetOrderReturnContextAsync(command.Request.OrderId, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<ReturnCreatedDto>(context.Error);
        }

        // C'EST ICI QUE LA GARDE MORD.
        if (context.Value.CustomerId != demandeur)
        {
            return Error.Validation(
                "return.order_not_found",
                "Aucune commande eligible ne correspond a cette demande.");
        }

        // CE QUE LA COMMANDE DIT NE SUFFIT PAS (ISSUE-014).
        var enCours = await _returns.ListOpenQuantitiesByOrderAsync(
            command.Request.OrderId, exceptReturnId: null, cancellationToken);

        var drafts = new List<ReturnItemDraft>();
        foreach (var requested in command.Request.Items)
        {
            var line = context.Value.Lines.FirstOrDefault(l => l.OrderItemId == requested.OrderItemId);
            if (line is null)
            {
                return Error.Validation("return.item_not_in_order", "Une ligne demandee n'appartient pas a la commande.");
            }

            var money = Money.Create(line.UnitPaidAmount, context.Value.Currency);
            if (money.IsFailure)
            {
                return Result.Failure<ReturnCreatedDto>(money.Error);
            }

            // Borné par le livré : au-delà, `ReturnItem.Create` calculerait une
            // disponibilité négative et rendrait la même erreur pour une raison
            // qu'on ne saurait plus lire.
            var dejaEngage = Math.Min(
                line.AlreadyReturnedQuantity + (enCours.TryGetValue(line.OrderItemId, out var ouvert) ? ouvert : 0),
                line.DeliveredQuantity);

            drafts.Add(new ReturnItemDraft(
                line.OrderItemId,
                line.ProductId,
                line.VariantId,
                line.Sku,
                line.Name,
                line.OrderedQuantity,
                line.DeliveredQuantity,
                dejaEngage,
                requested.Quantity,
                money.Value,
                requested.ReasonCode,
                requested.ConditionDeclared));
        }

        var firstProduct = context.Value.Lines.First();
        var policy = await _policies.GetApplicableSnapshotAsync(firstProduct.ProductId, firstProduct.CategoryId, cancellationToken);
        var estimated = Money.Create(drafts.Sum(i => i.UnitPaidAmount.Amount * i.RequestedQuantity), context.Value.Currency);
        if (estimated.IsFailure)
        {
            return Result.Failure<ReturnCreatedDto>(estimated.Error);
        }

        var created = ReturnRequest.Create(
            $"RET-{_clock.UtcNow:yyyyMMdd}-{Random.Shared.Next(1, 999999):000000}",
            context.Value.OrderId,
            context.Value.SellerOrderId,
            context.Value.CustomerId,
            context.Value.SellerId,
            context.Value.StoreId,
            command.Request.ResolutionRequested,
            command.Request.ReasonCode,
            command.Request.Comment,
            estimated.Value,
            policy,
            drafts,
            context.Value.DeliveredAtUtc,
            _clock.UtcNow);

        if (created.IsFailure)
        {
            return Result.Failure<ReturnCreatedDto>(created.Error);
        }

        await _returns.AddAsync(created.Value, command.Request.IdempotencyKey, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToCreated(created.Value);
    }

    private static ReturnCreatedDto ToCreated(ReturnRequest request)
        => new(
            request.Id,
            request.ReturnNumber,
            request.Status,
            new MoneyDto(request.EstimatedRefundAmount, request.Currency),
            request.Status.ToString(),
            request.ExpiresAtUtc);
}
