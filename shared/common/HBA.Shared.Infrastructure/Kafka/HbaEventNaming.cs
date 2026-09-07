using System.Collections.Concurrent;
using System.Reflection;
using HBA.Shared.IntegrationEvents;

namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>Nommage des événements et des topics selon le §19.2 du cahier des charges.</summary>
public static class HbaEventNaming
{
    private static readonly ConcurrentDictionary<Type, HbaEventAttribute?> Cache = new();

    /// <summary>
    /// Descripteur d'un type d'événement, ou null si le type ne porte pas
    /// <see cref="HbaEventAttribute"/> — c'est-à-dire s'il n'a pas encore été
    /// migré.
    /// </summary>
    public static HbaEventAttribute? Describe(Type eventType)
        => Cache.GetOrAdd(eventType, static t => t.GetCustomAttribute<HbaEventAttribute>(inherit: false));

    /// <summary>Vrai si l'événement suit le contrat du §19.</summary>
    public static bool IsCanonical(Type eventType) => Describe(eventType) is not null;

    /// <summary>
    /// Topic du §19.2 : <c>
    /// hba.&lt;env&gt;.&lt;domaine&gt;.&lt;agrégat&gt;.v&lt;major&gt;</c>.
    /// </summary>
    public static string Topic(HbaEventAttribute descriptor, string environment)
        => $"hba.{Normalize(environment)}.{descriptor.Domain}.{descriptor.Aggregate}.v{descriptor.Version}";

    /// <summary>Topic de lettres mortes associé (§19.7) : le topic métier suffixé `.dlq`.</summary>
    public static string DeadLetterTopic(HbaEventAttribute descriptor, string environment)
        => Topic(descriptor, environment) + ".dlq";

    /// <summary>Nom de schéma porté par `metadata.schema`, ex.</summary>
    public static string Schema(HbaEventAttribute descriptor)
        => $"hba.{descriptor.EventType}.v{descriptor.Version}";

    /// <summary>Type d'agrégat pour `aggregate.type`.</summary>
    public static string AggregateType(HbaEventAttribute descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.AggregateType))
        {
            return descriptor.AggregateType!;
        }

        var parts = descriptor.Aggregate.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(static p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    /// <summary>Normalise le nom d'environnement sur les trois valeurs du §19.1.</summary>
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
