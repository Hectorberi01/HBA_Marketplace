using Grpc.Core;
using HBA.Financial.Grpc.V1;
using HBA.Financial.Payments.Application.Payments.Commands;
using HBA.Shared.Hosting.Grpc;
using MediatR;

using System.Globalization;

// DEPLACE DEPUIS `HBA.Financial.Api.GrpcServices` (lot B de la migration gRPC).

namespace HBA.Financial.Api.Grpc.Services;

internal sealed class FinancialGrpcService : FinancialApi.FinancialApiBase
{
    private readonly ISender _sender;

    public FinancialGrpcService(ISender sender) => _sender = sender;

    public override async Task<FinancialOperationResponse> RefundPayment(RefundPaymentRequest request,ServerCallContext context)
    {
        if (!Guid.TryParse(request.PaymentId, out var paymentId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "payment_id n'est pas un GUID."));
        }

        var amount = string.IsNullOrWhiteSpace(request.Amount)
            ? (decimal?)null
            : ParseAmount(request.Amount, "amount");

        var returnId = ParseOptionalGuid(request.ReturnId, "return_id");
        var refundId = ParseOptionalGuid(request.RefundId, "refund_id");

        var result = await _sender.Send(
            new RefundPaymentCommand(
                paymentId,
                amount,
                string.IsNullOrWhiteSpace(request.Currency) ? null : request.Currency,
                string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason,
                string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey,
                returnId,
                refundId),
            context.CancellationToken);

        if (result.IsFailure)
        {
            // DEUX CHAMPS, PLUS UNE CHAÎNE « code:message ».
            return new FinancialOperationResponse
            {
                Succeeded = false,
                ReasonCode = result.Error.Code,
                Reason = result.Error.Message
            };
        }

        return new FinancialOperationResponse
        {
            Succeeded = true,
            Reason = "OK",
            ProviderReference = result.Value.ProviderRefundId,
            Status = result.Value.Status,
            Amount = result.Value.Amount.ToString(CultureInfo.InvariantCulture),
            Currency = result.Value.Currency,
            RefundId = result.Value.RefundId.ToString()
        };
    }

    /// <summary>Un montant venu du fil.</summary>
    private static decimal ParseAmount(string value, string field)
        => MontantSurLeFil.Lire(value, field);

    private static Guid? ParseOptionalGuid(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field} n'est pas un GUID."));
    }
}
