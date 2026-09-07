namespace HBA.Shared.IntegrationEvents;

/// <summary>
/// Déclare le nom métier d'un événement d'intégration selon le §19.2 : <c>
/// &lt;domaine&gt;.&lt;agrégat&gt;.&lt;action passée&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HbaEventAttribute : Attribute
{
    /// <param name="domain">
    /// Domaine métier : `identity`, `marketplace`, `food`, `delivery`, `payment`…
    /// </param>
    /// <param name="aggregate">Agrégat : `order`, `cart`, `product`, `delivery`…</param>
    /// <param name="action">
    /// Action au passé : `created`, `accepted`, `succeeded`, `cancelled`…
    /// </param>
    public HbaEventAttribute(string domain, string aggregate, string action)
    {
        Domain = domain;
        Aggregate = aggregate;
        Action = action;
        EventType = $"{domain}.{aggregate}.{action}";
    }

    /// <summary>
    /// Forme littérale, pour les événements que le cahier des charges nomme en DEUX
    /// segments.
    /// </summary>
    public HbaEventAttribute(string eventType)
    {
        var segments = eventType.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            throw new ArgumentException(
                $"Nom d'événement invalide : « {eventType} ». Au moins deux segments sont attendus.",
                nameof(eventType));
        }

        Domain = segments[0];
        Aggregate = segments.Length >= 3 ? segments[1] : segments[0];
        Action = segments[^1];
        EventType = eventType;
    }

    public string Domain { get; }

    public string Aggregate { get; }

    public string Action { get; }

    /// <summary>Version majeure du contrat.</summary>
    /// <summary>Version majeure du contrat.</summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// Type d'agrégat tel qu'il apparaît dans `aggregate.type` de l'enveloppe, en
    /// PascalCase : `FoodOrder`, `MarketplaceOrder`, `Payment`.
    /// </summary>
    public string? AggregateType { get; init; }

    /// <summary>Nom métier complet, ex. `food.order.accepted` ou `payment.succeeded`.</summary>
    public string EventType { get; }
}
