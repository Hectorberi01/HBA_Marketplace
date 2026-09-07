using HBA.Shared.IntegrationEvents;

namespace HBA.Returns.Contracts.IntegrationEvents;

/// <summary>Un remboursement a été VALIDÉ, mais l'argent n'est pas encore parti.</summary>
[HbaEvent("return-refund.return.refund.approved")]
public sealed record ReturnRefundApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>L'argent a RÉELLEMENT été versé à l'acheteur (référence FedaPay à l'appui).</summary>
[HbaEvent("return-refund.return.refunded")]
public sealed record ReturnRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid ReturnRequestId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal RefundAmount { get; init; }
    public required string Currency { get; init; }

    /// <summary>Référence du versement chez FedaPay : la preuve que l'argent est parti.</summary>
    public required string RefundReference { get; init; }

    /// <summary>
    /// Les lignes de commande que ce dossier a effectivement reprises, avec la
    /// quantité retenue — la reçue quand elle existe, la demandée sinon.
    /// </summary>
    public IReadOnlyCollection<ReturnedOrderLine> Lines { get; init; } = [];

    /// <summary>
    /// Ce que CE dossier a remboursé au total, versements cumulés, à l'instant où
    /// le message part.
    /// </summary>
    public decimal ReturnTotalRefundedAmount { get; init; }
}

/// <summary>Une ligne de commande reprise par un dossier de retour.</summary>
public sealed record ReturnedOrderLine
{
    public required Guid OrderItemId { get; init; }

    /// <summary>Quantité reprise, cumulée pour le dossier.</summary>
    public required int Quantity { get; init; }
}
