using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

namespace HBA.Marketplace.ReturnRefund.Application.Mappings;

public static class ReturnMappings
{
    public static ReturnRequestDto ToDto(this ReturnRequest request)
        => new(
            request.Id,
            request.ReturnNumber,
            request.OrderId,
            request.CustomerId,
            request.SellerId,
            request.StoreId,
            request.Status,
            request.ResolutionRequested,
            request.ReasonCode,
            new MoneyDto(request.EstimatedRefundAmount, request.Currency),
            request.ApprovedRefundAmount is null ? null : new MoneyDto(request.ApprovedRefundAmount.Value, request.Currency),
            request.ReturnShippingPayer,
            request.CreatedAtUtc,
            request.ExpiresAtUtc,
            request.ResolvedAtUtc,
            request.Items.Select(i => new ReturnItemDto(
                i.Id,
                i.OrderItemId,
                i.ProductId,
                i.VariantId,
                i.SkuSnapshot,
                i.NameSnapshot,
                i.RequestedQuantity,
                i.ReceivedQuantity,
                new MoneyDto(i.UnitPaidAmount, i.Currency),
                i.ReasonCode,
                i.ConditionDeclared,
                i.ConditionInspected)).ToList());

    public static ReturnTimelineEntryDto ToDto(this ReturnStatusHistory entry)
        => new(entry.Status, entry.Reason, entry.OccurredAtUtc, entry.ActorId);
}
