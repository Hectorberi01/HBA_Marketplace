namespace HBA.Marketplace.ReturnRefund.Infrastructure.Kafka.Consumers;

public sealed class ReturnRefundKafkaConsumers
{
    public static readonly string[] ConsumedTopics =
    [
        "marketplace.order.delivered",
        "payment.refund.succeeded",
        "payment.refund.failed",
        "delivery.return-picked-up",
        "delivery.return-delivered",
        "inventory.return-processed"
    ];
}
