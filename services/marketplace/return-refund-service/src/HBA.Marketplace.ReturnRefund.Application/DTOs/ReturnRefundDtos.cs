using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Application.DTOs;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record CreateReturnItemDto(
    Guid OrderItemId,
    int Quantity,
    ReturnReasonCode ReasonCode,
    InspectionCondition ConditionDeclared);

public sealed record CreateReturnRequestDto(
    Guid OrderId,
    ReturnResolution ResolutionRequested,
    ReturnReasonCode ReasonCode,
    string? Comment,
    IReadOnlyList<CreateReturnItemDto> Items,
    string IdempotencyKey);

public sealed record ReturnCreatedDto(
    Guid ReturnId,
    string ReturnNumber,
    ReturnStatus Status,
    MoneyDto EstimatedRefund,
    string NextAction,
    DateTime? ReturnDeadline);

public sealed record ReturnRequestDto(
    Guid Id,
    string ReturnNumber,
    Guid OrderId,
    Guid CustomerId,
    Guid SellerId,
    Guid StoreId,
    ReturnStatus Status,
    ReturnResolution ResolutionRequested,
    ReturnReasonCode ReasonCode,
    MoneyDto EstimatedRefund,
    MoneyDto? ApprovedRefund,
    string ReturnShippingPayer,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ResolvedAtUtc,
    IReadOnlyList<ReturnItemDto> Items);

public sealed record ReturnItemDto(
    Guid Id,
    Guid OrderItemId,
    Guid ProductId,
    Guid? VariantId,
    string SkuSnapshot,
    string NameSnapshot,
    int RequestedQuantity,
    int ReceivedQuantity,
    MoneyDto UnitPaid,
    ReturnReasonCode ReasonCode,
    InspectionCondition ConditionDeclared,
    InspectionCondition? ConditionInspected);

public sealed record ReturnTimelineEntryDto(ReturnStatus Status, string Reason, DateTime OccurredAtUtc, Guid? ActorId);

public sealed record AddEvidenceDto(string MediaId, string Kind, string? Caption);
public sealed record ReasonDto(string Reason);
public sealed record RegisterShipmentDto(string DeliveryId, string Mode, string? TrackingNumber);
public sealed record InspectReturnDto(InspectionCondition Condition, StockDisposition Disposition, string Notes);
public sealed record DecideRefundDto(decimal Amount, string Currency);

public sealed record OrderReturnSummaryDto(Guid OrderId, decimal ReturnedAmount, string Currency, int ActiveReturnCount);

// ═════════════════════════════════════════════════════════════════════════════
// CES DEUX ENREGISTREMENTS N'ONT PLUS D'APPELANT, ET C'EST DÉLIBÉRÉ.
//
// Les routes `/api/v1/admin/return-policies` qui les employaient ont été
// retirées le 23/08 : elles répondaient 200 et 201 sans rien persister — voir
// l'encadré de `Program.cs`. La politique de retour reste, elle, une constante
// rendue par `ReturnPolicyRepository`.
//
// On les conserve parce que la forme est juste et qu'elle a été relue : le lot
// qui rendra la politique configurable remontera des routes, et repartir de ces
// deux formes coûte moins que de les réinventer. Si ce lot n'arrive pas, ces
// vingt lignes sont le prix d'une option — pas d'un mensonge.
// ═════════════════════════════════════════════════════════════════════════════
public sealed record ReturnPolicyDto(
    string PolicyId,
    string Version,
    int ReturnWindowDays,
    bool AllowReturn,
    bool AllowRefundOnly,
    bool RequireEvidence,
    bool RequireInspection,
    decimal RestockingFeePercent);

public sealed record UpsertReturnPolicyDto(
    string ScopeType,
    string ScopeId,
    int ReturnWindowDays,
    bool AllowReturn,
    bool AllowRefundOnly,
    bool RequireEvidence,
    bool RequireInspection,
    decimal RestockingFeePercent);
