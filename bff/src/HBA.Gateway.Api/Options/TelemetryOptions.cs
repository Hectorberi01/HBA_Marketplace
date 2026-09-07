namespace HBA.Gateway.Api.Options;

/// <summary>Export OpenTelemetry.</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Nom du service dans les traces.</summary>
    public string ServiceName { get; init; } = "hba-gateway";

    /// <summary>Point d'entrée OTLP du collecteur.</summary>
    public string? Endpoint { get; init; }
}
