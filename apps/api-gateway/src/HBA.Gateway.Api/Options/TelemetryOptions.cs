namespace HBA.Gateway.Api.Options;

/// <summary>Export OpenTelemetry.</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Nom du service dans les traces. Doit rester stable entre versions.</summary>
    public string ServiceName { get; init; } = "hba-gateway";

    /// <summary>
    /// Point d'entrée OTLP du collecteur. Vide = export désactivé.
    /// </summary>
    /// <remarks>
    /// VIDE PAR DÉFAUT, ET C'EST VOLONTAIRE.
    ///
    /// Avec une adresse par défaut codée en dur, l'exécution d'un test ou d'un
    /// `dotnet run` local tente d'atteindre `otel-collector:4317`, qui n'existe
    /// pas hors du réseau Docker. L'exportateur réessaie en tâche de fond et
    /// remplit la console de messages d'erreur sans rapport avec ce que l'on
    /// débogue. L'instrumentation reste active — seul l'export est coupé.
    /// </remarks>
    public string? Endpoint { get; init; }
}
