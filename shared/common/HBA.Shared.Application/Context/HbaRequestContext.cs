namespace HBA.Shared.Application.Context;

/// <summary>
/// Contexte propagé du §18 du cahier des charges : ce qui accompagne une requête
/// du bord HTTP jusqu'aux appels gRPC sortants et aux événements Kafka publiés.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE TYPE VIT DANS `Application` ET NON DANS `Hosting`.
///
/// Il est lu par trois couches qui n'ont pas les mêmes dépendances : le bord HTTP
/// le remplit, les use cases le consultent pour l'acteur, et l'infrastructure le
/// recopie dans l'enveloppe Kafka. `Hosting` référence ASP.NET ; `Infrastructure`
/// ne le référence pas et ne doit pas commencer. `Application` est le seul projet
/// que les trois voient déjà.
///
/// C'est aussi la raison de l'AsyncLocal plutôt que d'une injection : un publisher
/// d'outbox s'exécute dans un BackgroundService, hors de tout scope de requête,
/// et doit malgré tout retrouver le correlationId d'origine — sinon la chaîne
/// causale se coupe exactement à l'endroit où elle devient asynchrone, c'est-à-dire
/// là où on en a le plus besoin pour comprendre un incident.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record HbaRequestContext
{
    /// <summary>Identifiant de la requête entrante, renvoyé dans `meta.requestId` (§5).</summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// Identifiant commun à tout un flux métier distribué (§19.1 `correlationId`).
    /// Repris tel quel d'un appel à l'autre : c'est lui qui relie un checkout,
    /// le paiement qu'il déclenche et la livraison qui en découle.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Trace OpenTelemetry propagée HTTP -> gRPC -> Kafka (§2, §19.1).</summary>
    public string? TraceId { get; init; }

    /// <summary>Commande ou événement ayant causé l'action en cours (§19.1 `causationId`).</summary>
    public string? CausationId { get; init; }

    /// <summary>Acteur à l'origine de l'action. Null pour un traitement système.</summary>
    public HbaActor? Actor { get; init; }

    /// <summary>
    /// Clé d'idempotence fournie par le client (§5). Obligatoire sur les POST de
    /// création, de paiement et de checkout ; recommandée ailleurs.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Locale de la requête, ex. `fr-BJ`. Sert au rendu des notifications.</summary>
    public string Locale { get; init; } = "fr-BJ";

    /// <summary>Périmètre logique de la donnée (§19.1 `tenantId`), ex. `hba-bj`.</summary>
    public string TenantId { get; init; } = "hba-bj";

    /// <summary>Nom du service courant, reporté en `producer` dans les événements.</summary>
    public string? ServiceName { get; init; }

    /// <summary>
    /// Préfixe du service pour les codes `*_SERVICE_NOT_FOUND` du §10, ex. `USER`.
    ///
    /// Distinct de <see cref="ServiceName"/> parce que les deux ne coïncident pas
    /// toujours : le dossier `cart-service` porte le code `MARKETPLACE_CART`, et
    /// `seller-service` porte `MERCHANT`. Déduire l'un de l'autre marcherait pour
    /// douze services sur seize et produirait silencieusement un code inconnu du
    /// contrat pour les quatre autres.
    /// </summary>
    public string? ServiceCode { get; init; }

    private static readonly AsyncLocal<HbaRequestContext?> Ambient = new();

    /// <summary>
    /// Contexte courant. Jamais null : hors requête, renvoie un contexte vide plutôt
    /// que de forcer chaque appelant à tester. Un correlationId vide se voit dans les
    /// journaux ; une NullReferenceException dans un consumer Kafka arrête la boucle.
    /// </summary>
    public static HbaRequestContext Current => Ambient.Value ?? Empty;

    /// <summary>Contexte neutre, utilisé hors de toute requête entrante.</summary>
    public static readonly HbaRequestContext Empty = new();

    /// <summary>
    /// Installe <paramref name="context"/> comme contexte courant jusqu'à la libération
    /// du scope retourné. Le scope restaure la valeur précédente, ce qui rend
    /// l'imbrication sûre — un consumer qui traite un message à l'intérieur d'une
    /// requête HTTP ne laisse pas son contexte derrière lui.
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

    /// <summary>Identifiant de l'acteur : id utilisateur, ou nom du service pour un acteur système.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Rôles portés par le jeton, tels quels.</summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
