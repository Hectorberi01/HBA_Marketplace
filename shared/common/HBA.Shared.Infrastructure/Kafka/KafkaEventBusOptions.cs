namespace HBA.Shared.Infrastructure.Kafka;

public sealed class KafkaEventBusOptions
{
    public const string SectionName = "Kafka";

    public string? BootstrapServers { get; init; }

    public string TopicPrefix { get; init; } = "service";

    public string TopicVersion { get; init; } = "v1";

    public string? Producer { get; init; }

    public string? ProducerVersion { get; init; }

    public bool Enabled { get; init; } = true;

    public string? ConsumerGroup { get; init; }

    /// <summary>Les sujets auxquels ce service s'abonne.</summary>
    public string[] SubscribeTopics { get; init; } = [];

    /// <summary>« LISTE VIDE » ET « PAS DE LISTE » NE VEULENT PAS DIRE LA MEME CHOSE.</summary>
    public bool AbonnementsDeclares { get; init; }
}
