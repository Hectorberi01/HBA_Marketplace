namespace HBA.Shared.Hosting.Telemetry;

/// <summary>Export OpenTelemetry d'un service.</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Nom du service dans les traces.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Point d'entrée OTLP du collecteur.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Journaux envoyés au collecteur OTLP en plus de la console.</summary>
    public bool ExportLogs { get; init; } = true;

    /// <summary>Journaux console au format JSON plutôt qu'en texte lisible.</summary>
    public bool JsonConsole { get; init; }
}
