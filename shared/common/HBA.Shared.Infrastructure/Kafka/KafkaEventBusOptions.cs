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

    /// <summary>
    /// Les sujets auxquels ce service s'abonne. VIDE par défaut : le catalogue
    /// <see cref="HbaTopics"/> fait foi.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE PROPRIÉTÉ PORTAIT UNE LISTE DE TREIZE SUJETS ÉCRITE EN DUR, ET
    ///    C'ÉTAIT LA SECONDE SOURCE DE VÉRITÉ D'ISSUE-001.
    ///
    /// Elle disait `service.merchant.v1`, `service.commerce.v1`,
    /// `service.financial.v1`… pendant que les producteurs dérivaient leur sujet de
    /// `SERVICE_NAME` et publiaient sur `service.seller.v1`, `service.cart.v1`,
    /// `service.payment.v1`. Les deux listes étaient justes chacune de son côté ;
    /// elles avaient cessé de se correspondre, et rien ne pouvait le signaler.
    ///
    /// Vide, le consommateur prend `HbaTopics.Tous(options)` — la même table que
    /// celle qui décide du sujet de publication. Une seule dérivation, appelée des
    /// deux côtés.
    ///
    /// LA RENSEIGNER RESTE POSSIBLE, et reste un choix DÉLIBÉRÉ : un service qui
    /// n'écoute qu'une poignée de sujets consomme moins. Mais il n'entendra plus
    /// jamais un domaine ajouté au catalogue, et c'est à lui de le savoir.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string[] SubscribeTopics { get; init; } = [];
}
