using System.Globalization;
using HBA.Financial.Grpc.V1;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.PaymentClient;

internal sealed class PaymentGrpcClient : IPaymentGrpcClient
{
    private readonly FinancialApi.FinancialApiClient _client;

    public PaymentGrpcClient(FinancialApi.FinancialApiClient client) => _client = client;

    public async Task<Result<PaymentRefundResult>> RefundPaymentAsync(
        string paymentId,
        Guid returnId,
        Guid refundId,
        Money amount,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var response = await _client.RefundPaymentAsync(
            new RefundPaymentRequest
            {
                PaymentId = paymentId,
                ReturnId = returnId.ToString(),
                RefundId = refundId.ToString(),
                Amount = amount.Amount.ToString(CultureInfo.InvariantCulture),
                Currency = amount.Currency,
                Reason = reason,
                IdempotencyKey = idempotencyKey
            },
            cancellationToken: cancellationToken);

        if (!response.Succeeded)
        {
            // ═════════════════════════════════════════════════════════════════
            // LE CODE D'ERREUR DE PAYMENT SURVIT MAINTENANT AU SAUT gRPC.
            //
            // Il arrivait empaqueté dans `reason` sous la forme « code:message »,
            // que personne ne reparsait : le refus était donc TOUJOURS rendu sous
            // le même code local, `payment_refund_failed`. « Paiement déjà
            // intégralement remboursé », « montant supérieur au remboursable » et
            // « paiement introuvable » devenaient le même échec, indiscernables
            // pour l'appelant comme pour l'exploitation.
            //
            // On préfixe plutôt que de recopier tel quel : le code reste
            // attribuable à son émetteur — `return_refund.payment.<code>` dit à la
            // fois où l'erreur a été constatée et qui l'a produite.
            // ═════════════════════════════════════════════════════════════════
            var code = string.IsNullOrWhiteSpace(response.ReasonCode)
                ? "return_refund.payment_refund_failed"
                : $"return_refund.payment.{response.ReasonCode}";

            return Error.Failure(
                code,
                string.IsNullOrWhiteSpace(response.Reason)
                    ? "Le service Payment a refuse le remboursement."
                    : response.Reason);
        }

        if (!decimal.TryParse(response.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var refundedAmount))
        {
            refundedAmount = amount.Amount;
        }

        return new PaymentRefundResult(
            string.IsNullOrWhiteSpace(response.ProviderReference) ? response.RefundId : response.ProviderReference,
            string.IsNullOrWhiteSpace(response.Status) ? "Succeeded" : response.Status,
            refundedAmount,
            string.IsNullOrWhiteSpace(response.Currency) ? amount.Currency : response.Currency);
    }
}
