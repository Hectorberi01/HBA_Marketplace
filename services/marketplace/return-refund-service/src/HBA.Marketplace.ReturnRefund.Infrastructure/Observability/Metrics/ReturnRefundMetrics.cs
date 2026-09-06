namespace HBA.Marketplace.ReturnRefund.Infrastructure.Observability;

public static class ReturnRefundMetrics
{
    public const string ReturnRequestRate = "return_request_rate";
    public const string RefundProcessingDelay = "refund_processing_delay";
    public const string OutboxBacklog = "return_refund_outbox_backlog";
}
