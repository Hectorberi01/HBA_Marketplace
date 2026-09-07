namespace HBA.Shared.Application.Context;

/// <summary>
/// Contexte propagé du §18 du cahier des charges : ce qui accompagne une requête du
/// bord HTTP jusqu'aux appels gRPC sortants et aux événements Kafka publiés.
/// </summary>
public sealed record HbaRequestContext
{
    /// <summary>Identifiant de la requête entrante, renvoyé dans `meta.requestId` (§5).</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// Identifiant commun à tout un flux métier distribué (§19.1 `correlationId`).
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Trace OpenTelemetry propagée HTTP -> gRPC -> Kafka (§2, §19.1).</summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Commande ou événement ayant causé l'action en cours (§19.1 `causationId`).
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>Acteur à l'origine de l'action.</summary>
    public HbaActor? Actor { get; init; }

    /// <summary>Clé d'idempotence fournie par le client (§5).</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Locale de la requête, ex.</summary>
    public string Locale { get; init; } = "fr-BJ";

    /// <summary>Périmètre logique de la donnée (§19.1 `tenantId`), ex.</summary>
    public string TenantId { get; init; } = "hba-bj";

    /// <summary>Nom du service courant, reporté en `producer` dans les événements.</summary>
    public string? ServiceName { get; init; }

    /// <summary>Préfixe du service pour les codes `*_SERVICE_NOT_FOUND` du §10, ex.</summary>
    public string? ServiceCode { get; init; }

    private static readonly AsyncLocal<HbaRequestContext?> Ambient = new();

    /// <summary>
    /// Contexte courant. Jamais null : hors requête, renvoie un contexte vide
    /// plutôt que de forcer chaque appelant à tester.
    /// </summary>
    public static HbaRequestContext Current => Ambient.Value ?? Empty;

    /// <summary>Contexte neutre, utilisé hors de toute requête entrante.</summary>
    public static readonly HbaRequestContext Empty = new();

    /// <summary>
    /// Installe <paramref name="context"/> comme contexte courant jusqu'à la
    /// libération du scope retourné.
    /// </summary>
    public static IDisposable BeginScope(HbaRequestContext context)
    {
        var previous = Ambient.Value;
        Ambient.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly HbaRequestContext? _previous;
        private bool _disposed;

        public Scope(HbaRequestContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}

/// <summary>Acteur d'une action : utilisateur authentifié, service, ou système.</summary>
public sealed record HbaActor
{
    /// <summary>`CUSTOMER`, `SELLER`, `DRIVER`, `ADMIN`, `SYSTEM`… (§19.1 `actor.type`).</summary>
    public string Type { get; init; } = "SYSTEM";

    /// <summary>
    /// Identifiant de l'acteur : id utilisateur, ou nom du service pour un acteur
    /// système.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Rôles portés par le jeton, tels quels.</summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
