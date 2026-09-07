using System.Diagnostics;

namespace HBA.Shared.Infrastructure.Observability;

/// <summary>LES SOURCES D'INSTRUMENTATION MAISON DE LA PLATEFORME.</summary>
public static class HbaTelemetry
{
    /// <summary>Publication et consommation d'événements d'intégration (§19).</summary>
    public const string KafkaSourceName = "Hba.Kafka";

    /// <summary>
    /// Source d'activités de la messagerie, partagée par le publieur et le
    /// consommateur.
    /// </summary>
    public static readonly ActivitySource Kafka = new(KafkaSourceName);
}
