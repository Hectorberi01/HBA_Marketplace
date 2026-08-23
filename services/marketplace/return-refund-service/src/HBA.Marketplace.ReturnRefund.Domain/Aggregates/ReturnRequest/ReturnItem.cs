using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class ReturnItem : Entity<Guid>
{
    private ReturnItem()
    {
    }

    private ReturnItem(ReturnItemDraft draft)
        : base(Guid.NewGuid())
    {
        OrderItemId = draft.OrderItemId;
        ProductId = draft.ProductId;
        VariantId = draft.VariantId;
        SkuSnapshot = draft.SkuSnapshot;
        NameSnapshot = draft.NameSnapshot;
        OrderedQuantity = draft.OrderedQuantity;
        DeliveredQuantity = draft.DeliveredQuantity;
        AlreadyReturnedQuantity = draft.AlreadyReturnedQuantity;
        RequestedQuantity = draft.RequestedQuantity;
        UnitPaidAmount = draft.UnitPaidAmount.Amount;
        Currency = draft.UnitPaidAmount.Currency;
        ReasonCode = draft.ReasonCode;
        ConditionDeclared = draft.ConditionDeclared;
    }

    public Guid ReturnId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public string SkuSnapshot { get; private set; } = string.Empty;
    public string NameSnapshot { get; private set; } = string.Empty;
    public int OrderedQuantity { get; private set; }
    public int DeliveredQuantity { get; private set; }
    public int AlreadyReturnedQuantity { get; private set; }
    public int RequestedQuantity { get; private set; }
    public int ReceivedQuantity { get; private set; }
    public decimal UnitPaidAmount { get; private set; }
    public string Currency { get; private set; } = "XOF";
    public ReturnReasonCode ReasonCode { get; private set; }
    public InspectionCondition ConditionDeclared { get; private set; }
    public InspectionCondition? ConditionInspected { get; private set; }

    public static Result<ReturnItem> Create(ReturnItemDraft draft)
    {
        if (draft.OrderItemId == Guid.Empty || draft.ProductId == Guid.Empty)
        {
            return Error.Validation("return.item.identity_required", "La ligne de commande et le produit sont obligatoires.");
        }

        var available = draft.DeliveredQuantity - draft.AlreadyReturnedQuantity;
        if (draft.RequestedQuantity <= 0 || draft.RequestedQuantity > available)
        {
            return Error.Validation("return.item.quantity_invalid", "La quantite demandee depasse la quantite disponible.");
        }

        if (string.IsNullOrWhiteSpace(draft.NameSnapshot))
        {
            return Error.Validation("return.item.name_required", "Le nom snapshot de l'article est obligatoire.");
        }

        return new ReturnItem(draft);
    }

    public void MarkReceived(int quantity)
    {
        ReceivedQuantity = Math.Clamp(quantity, 0, RequestedQuantity);
    }
}
