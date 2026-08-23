using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Abstractions;

public interface IReturnRefundUnitOfWork : IUnitOfWork;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed record OrderReturnContext(
    Guid OrderId,
    Guid CustomerId,
    Guid SellerId,
    Guid StoreId,
    Guid? SellerOrderId,
    DateTime DeliveredAtUtc,
    string PaymentId,
    string Currency,
    decimal CapturedAmount,
    decimal AlreadyRefundedAmount,
    IReadOnlyList<OrderReturnLineContext> Lines);

public sealed record OrderReturnLineContext(
    Guid OrderItemId,
    Guid ProductId,
    Guid? VariantId,
    Guid CategoryId,
    string Sku,
    string Name,
    int OrderedQuantity,
    int DeliveredQuantity,
    int AlreadyReturnedQuantity,
    decimal UnitPaidAmount);

public interface IOrderGrpcClient
{
    Task<Result<OrderReturnContext>> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed record PaymentRefundResult(string ProviderRefundId, string Status, decimal Amount, string Currency);

public interface IPaymentGrpcClient
{
    Task<Result<PaymentRefundResult>> RefundPaymentAsync(
        string paymentId,
        Guid returnId,
        Guid refundId,
        Money amount,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public interface IInventoryGrpcClient
{
    Task<Result> ProcessReturnedStockAsync(Guid returnId, Guid orderItemId, StockDisposition disposition, CancellationToken cancellationToken);
}

public interface IDeliveryGrpcClient
{
    Task<Result<string>> CreateReturnDeliveryAsync(Guid returnId, Guid orderId, Guid sellerId, Guid customerId, CancellationToken cancellationToken);
}

public interface IMediaGrpcClient
{
    Task<Result> ValidateMediaAsync(string mediaId, Guid ownerId, CancellationToken cancellationToken);
}

// ═════════════════════════════════════════════════════════════════════════════
// `IReturnPolicyApplicationService` A ÉTÉ RETIRÉ AVEC LES ROUTES QU'IL DEVAIT
//    SERVIR.
//
// Il déclarait `ListAsync` et `UpsertAsync` sur les politiques de retour.
// AUCUNE classe ne l'implémentait, et aucun code ne l'injectait : les deux
// routes d'administration, elles, faisaient le travail à la main dans des
// lambdas — et le faisaient faux, puisqu'elles ne persistaient rien.
//
// Une interface sans implémentation n'est pas une intention documentée : c'est
// une promesse que la relecture prend pour un contrat existant. Le jour où la
// politique de retour deviendra configurable, elle se réécrira avec le lot —
// et `ReturnPolicyDto` / `UpsertReturnPolicyDto` sont conservés pour cela.
// ═════════════════════════════════════════════════════════════════════════════
