namespace HBA.Returns.Contracts;

/// <summary>Vue publique d'une demande de retour.</summary>
/// <param name="RefundableAmount">
/// PLAFOND remboursable — total de la ligne de commande, figé à la création.
/// </param>
public sealed record ReturnRequestSummary(
    Guid Id,
    Guid OrderId,
    Guid OfferId,
    Guid BuyerId,
    Guid SellerId,
    string Reason,
    string Status,
    string Currency,
    decimal RefundableAmount,

    decimal? RefundAmount,
    string? Carrier,
    string? TrackingNumber,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

/// <summary>
/// Un remboursement effectif (retour au statut Refunded) : commande concernée,
/// montant remboursé et date du remboursement.
/// </summary>
public sealed record SellerRefundLine(
    Guid OrderId,
    decimal RefundAmount,
    string Currency,
    DateTime RefundedAtUtc);

/// <summary>Un remboursement VALIDÉ dont l'argent n'est PAS ENCORE PARTI.</summary>
public sealed record PendingRefundLine(
    Guid ReturnRequestId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    decimal RefundAmount,
    string Currency,
    DateTime ApprovedAtUtc);
