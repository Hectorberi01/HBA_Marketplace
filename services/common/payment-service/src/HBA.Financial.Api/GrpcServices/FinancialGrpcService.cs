using HBA.Shared.Hosting.Grpc;
using System.Globalization;
using Grpc.Core;
using HBA.Financial.Grpc.V1;
using HBA.Financial.Payments.Application.Payments.Commands;
using MediatR;

namespace HBA.Financial.Api.GrpcServices;

public sealed class FinancialGrpcService : FinancialApi.FinancialApiBase
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
            //
            // Le séparateur n'était même pas le même qu'ailleurs (« — » côté
            // delivery), pour la même idée. Aucun appelant ne reparsait ni l'un ni
            // l'autre : le code normalisé était perdu, et seul un texte destiné à
            // un humain survivait au saut gRPC.
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

    /// <summary>
    /// Un montant venu du fil.
    /// </summary>
    /// <remarks>
    /// CETTE FONCTION ÉTAIT LA SEULE DES HUIT À REFUSER AU LIEU DE RENDRE ZÉRO.
    ///
    /// Elle avait raison, et c'est son comportement qui a été généralisé aux sept
    /// autres — voir <see cref="MontantSurLeFil"/>. Elle délègue désormais, pour
    /// que la règle vive à un seul endroit : c'est ici qu'elle divergerait en
    /// premier, ce service étant celui qui manipule réellement de l'argent.
    /// </remarks>
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
