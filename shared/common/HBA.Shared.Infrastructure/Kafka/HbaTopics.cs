namespace HBA.Shared.Infrastructure.Kafka;

/// <summary>
/// Le catalogue des sujets Kafka de la plateforme — UNE seule table, lue par le
/// producteur ET par le consommateur.
/// </summary>
public static class HbaTopics
{
    /// <summary>Le domaine de chaque service qui publie.</summary>
    public static readonly IReadOnlyDictionary<string, string> DomaineParService =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // analytics-service NE PUBLIE RIEN, ET IL EST INSCRIT QUAND MEME.
            ["analytics-service"] = "analytics",

            ["seller-service"] = "merchant",
            ["cart-service"] = "commerce",
            ["payment-service"] = "financial",
            ["review-service"] = "engagement",
            ["notification-service"] = "communication",
            ["restaurant-service"] = "food",
            ["identity-service"] = "identity",
            ["user-service"] = "user",
            ["catalog-service"] = "catalog",
            ["inventory-service"] = "inventory",
            ["order-service"] = "order",
            ["delivery-service"] = "delivery",
            ["media-service"] = "media",
            ["promotion-service"] = "promotion",
            ["food-cart-service"] = "food-cart",
            ["food-order-service"] = "food-order",
            ["return-refund-service"] = "return-refund",
            ["delivery-pricing-service"] = "delivery-pricing",
            ["driver-service"] = "driver",
            ["route-service"] = "route",

            // Les déploiements nomment leurs conteneurs par DOMAINE —
            // `merchant-service`, `commerce-service`, `financial-service` — là où
            // `docker-compose.dev.yml` les nomme par dépôt : `seller-service`,
            // `cart-service`, `payment-service`.
            ["merchant-service"] = "merchant",
            ["commerce-service"] = "commerce",
            ["financial-service"] = "financial",
            ["communication-service"] = "communication",
            ["engagement-service"] = "engagement",
            ["food-service"] = "food"
        };

    /// <summary>Le service est-il inscrit au catalogue ?</summary>
    public static bool EstConnu(string? serviceOuProducteur)
        => !string.IsNullOrWhiteSpace(serviceOuProducteur)
           && DomaineParService.ContainsKey(serviceOuProducteur);

    /// <summary>Le domaine d'un service.</summary>
    public static string Domaine(string serviceOuProducteur)
        => DomaineParService.TryGetValue(serviceOuProducteur, out var domaine)
            ? domaine
            : serviceOuProducteur.Replace("-service", string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>Le sujet d'un service : <c>{prefixe}.{domaine}.{version}</c>.</summary>
    public static string Pour(KafkaEventBusOptions options, string serviceOuProducteur)
        => $"{options.TopicPrefix}.{Domaine(serviceOuProducteur)}.{options.TopicVersion}";

    /// <summary>Tous les sujets de la plateforme, sans doublon et triés.</summary>
    public static IReadOnlyList<string> Tous(KafkaEventBusOptions options)
        => [.. DomaineParService.Values
            .Distinct(StringComparer.Ordinal)
            .Select(domaine => $"{options.TopicPrefix}.{domaine}.{options.TopicVersion}")
            .OrderBy(sujet => sujet, StringComparer.Ordinal)];
}
