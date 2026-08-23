using System.Collections.Concurrent;
using System.Reflection;
using HBA.Shared.IntegrationEvents;

namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>
/// Nommage des événements et des topics selon le §19.2 du cahier des charges.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI CHANGE PAR RAPPORT À `KafkaEventNaming`, ET POURQUOI ÇA COMPTE.
///
/// Topic actuel : `{prefix}.{service-producteur}.{version}` — UN TOPIC PAR SERVICE.
/// Topic exigé  : `hba.&lt;env&gt;.&lt;domaine&gt;.&lt;agrégat&gt;.v&lt;major&gt;` — UN TOPIC PAR AGRÉGAT.
///
/// La différence n'est pas cosmétique. Avec un topic par service, un consumer qui
/// ne s'intéresse qu'aux commandes reçoit aussi les paniers, les produits et les
/// avis du même producteur, et doit tout désérialiser pour jeter la majorité. La
/// rétention, le partitionnement et les droits de lecture se règlent alors pour le
/// producteur entier au lieu de l'agrégat : impossible de garder les paiements
/// trente jours et les positions GPS une heure.
///
/// Conséquence directe pour la migration : un consumer écrit selon la spec
/// s'abonne à `hba.prod.food.order.v1` et ne reçoit RIEN du dispositif actuel, qui
/// publie sur `hba.food.v1`. Les deux mondes ne se croisent pas — d'où la bascule
/// événement par événement plutôt qu'un basculement global.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class HbaEventNaming
{
    private static readonly ConcurrentDictionary<Type, HbaEventAttribute?> Cache = new();

    /// <summary>
    /// Descripteur d'un type d'événement, ou null si le type ne porte pas
    /// <see cref="HbaEventAttribute"/> — c'est-à-dire s'il n'a pas encore été migré.
    /// </summary>
    public static HbaEventAttribute? Describe(Type eventType)
        => Cache.GetOrAdd(eventType, static t => t.GetCustomAttribute<HbaEventAttribute>(inherit: false));

    /// <summary>Vrai si l'événement suit le contrat du §19.</summary>
    public static bool IsCanonical(Type eventType) => Describe(eventType) is not null;

    /// <summary>
    /// Topic du §19.2 : <c>hba.&lt;env&gt;.&lt;domaine&gt;.&lt;agrégat&gt;.v&lt;major&gt;</c>.
    /// </summary>
    public static string Topic(HbaEventAttribute descriptor, string environment)
        => $"hba.{Normalize(environment)}.{descriptor.Domain}.{descriptor.Aggregate}.v{descriptor.Version}";

    /// <summary>
    /// Topic de lettres mortes associé (§19.7) : le topic métier suffixé `.dlq`.
    /// Un DLQ par agrégat et non par service, pour la même raison que les topics :
    /// on veut pouvoir rejouer les paiements morts sans rejouer les positions GPS.
    /// </summary>
    public static string DeadLetterTopic(HbaEventAttribute descriptor, string environment)
        => Topic(descriptor, environment) + ".dlq";

    /// <summary>Nom de schéma porté par `metadata.schema`, ex. `hba.food.order.accepted.v1`.</summary>
    public static string Schema(HbaEventAttribute descriptor)
        => $"hba.{descriptor.EventType}.v{descriptor.Version}";

    /// <summary>
    /// Type d'agrégat pour `aggregate.type`. Utilise <see cref="HbaEventAttribute.AggregateType"/>
    /// s'il est renseigné, sinon met le segment agrégat en PascalCase.
    /// </summary>
    public static string AggregateType(HbaEventAttribute descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.AggregateType))
        {
            return descriptor.AggregateType!;
        }

        var parts = descriptor.Aggregate.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(static p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>
    /// Normalise le nom d'environnement sur les trois valeurs du §19.1.
    /// Tout ce qui n'est ni `staging` ni `production` est traité comme `local` :
    /// un environnement mal orthographié doit publier à côté de la production,
    /// jamais dedans.
    /// </summary>
    public static string Normalize(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return "local";
        }

        var value = environment.Trim().ToLowerInvariant();

        return value switch
        {
            "production" or "prod" => "production",
            "staging" or "stage" => "staging",
            _ => "local"
        };
    }
}
