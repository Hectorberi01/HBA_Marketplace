namespace HBA.Shared.Hosting.Telemetry;

/// <summary>
/// Export OpenTelemetry d'un service. Section de configuration : « OpenTelemetry ».
///
/// ═════════════════════════════════════════════════════════════════════════════
/// MÊME FORME QUE `TelemetryOptions` DE LA PASSERELLE, ET C'EST DÉLIBÉRÉ.
///
/// La passerelle a la sienne depuis l'origine ; les quatorze services n'avaient
/// rien. Reprendre la même section et la même clé d'environnement
/// (`OPENTELEMETRY__ENDPOINT`) évite qu'un exploitant ait à retenir deux noms pour
/// le même réglage — c'est le genre d'écart qui fait qu'un service reste muet
/// après un déploiement et que personne ne sait pourquoi.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Nom du service dans les traces. Doit rester stable entre versions.
    /// </summary>
    /// <remarks>
    /// VIDE PAR DÉFAUT, ET RÉSOLU DEPUIS `SERVICE_NAME`.
    ///
    /// Le nom du service existe déjà dans l'environnement : `docker-compose.dev.yml`
    /// pose `SERVICE_NAME` sur les quatorze services, et `KafkaEventNaming` s'en sert
    /// pour le champ `producer` des événements. Le redemander ici créerait une
    /// SECONDE source de vérité — et le jour où les deux divergeraient, les traces
    /// d'un service porteraient un nom que ses événements ne portent pas. La
    /// corrélation entre une trace et le message qu'elle a produit deviendrait
    /// manuelle.
    /// </remarks>
    public string? ServiceName { get; init; }

    /// <summary>
    /// Point d'entrée OTLP du collecteur. Vide = export désactivé.
    /// </summary>
    /// <remarks>
    /// VIDE PAR DÉFAUT, ET C'EST VOLONTAIRE.
    ///
    /// Avec une adresse par défaut codée en dur, un `dotnet test` ou un
    /// `dotnet run` local tente d'atteindre `otel-collector:4317`, qui n'existe pas
    /// hors du réseau Docker. L'exportateur réessaie en tâche de fond et remplit la
    /// console de messages d'erreur sans rapport avec ce que l'on débogue.
    ///
    /// L'INSTRUMENTATION RESTE ACTIVE — SEUL L'EXPORT EST COUPÉ.
    ///
    /// La distinction compte : les `Activity` continuent d'être créées, donc
    /// `Activity.Current` reste renseignée, donc l'en-tête `traceparent` posé par le
    /// publieur Kafka et l'identifiant de corrélation des journaux gardent une
    /// valeur. Couper l'instrumentation elle-même les viderait, et l'on déboguerait
    /// en local un comportement de propagation différent de celui de production.
    /// </remarks>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Journaux envoyés au collecteur OTLP en plus de la console.
    /// </summary>
    /// <remarks>
    /// Séparé de <see cref="Endpoint"/> parce que les volumes n'ont rien à voir :
    /// on veut souvent les traces d'un environnement sans y déverser aussi chaque
    /// ligne de journal. Sans point d'entrée, ce drapeau n'a de toute façon aucun
    /// effet.
    /// </remarks>
    public bool ExportLogs { get; init; } = true;

    /// <summary>
    /// Journaux console au format JSON plutôt qu'en texte lisible.
    /// </summary>
    /// <remarks>
    /// FAUX PAR DÉFAUT, ET ACTIVÉ PAR LE COMPOSE / LES MANIFESTS.
    ///
    /// Le JSON est ce que Loki et les agents de collecte savent découper en champs ;
    /// le texte est ce qu'un développeur peut lire dans son terminal. Imposer le JSON
    /// partout rendrait `docker compose logs` illisible pendant le développement, et
    /// c'est précisément là qu'on lit le plus de journaux à l'œil.
    /// </remarks>
    public bool JsonConsole { get; init; }
}
