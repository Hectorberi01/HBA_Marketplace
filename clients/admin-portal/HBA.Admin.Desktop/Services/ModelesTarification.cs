using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Une règle de tarification de course, telle que `PricingRule` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE SEULE RÈGLE S'APPLIQUE À TOUTE LA PLATEFORME, ET CE N'EST PAS ÉVIDENT.
///
/// `EfDeliveryPricingStore.CreateQuoteAsync` choisit ainsi :
///
///     .Where(r =&gt; r.Status == "ACTIVE"
///              &amp;&amp; r.ActiveFrom &lt;= UtcNow
///              &amp;&amp; (r.ActiveTo == null || r.ActiveTo &gt; UtcNow))
///     .OrderByDescending(r =&gt; r.Priority)
///     .FirstAsync()
///
/// `Scope`, `ServiceLevel` et `VehicleType` N'APPARAISSENT PAS dans ce filtre.
/// Ils sont stockés, rendus au contrat, affichés — et ne participent PAS au
/// choix. Une règle « EXPRESS » de priorité 200 tarife donc aussi les courses
/// STANDARD, et une règle « MOTORBIKE » tarife les voitures.
///
/// CONSÉQUENCE POUR L'ÉCRAN : la priorité est le seul réglage qui décide, et la
/// console indique explicitement quelle règle gagne à l'instant présent. Sans
/// cela, on croit régler la tarification des livraisons express en créant une
/// règle qui reprend en fait toutes les courses.
///
/// À ÉGALITÉ DE PRIORITÉ, AUCUN DÉPARTAGE N'EST DÉFINI. `OrderByDescending` seul
/// laisse PostgreSQL rendre l'une ou l'autre — et pas forcément la même deux
/// fois. L'écran signale les priorités en doublon.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record RegleTarifaire(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("serviceLevel")] string ServiceLevel,
    [property: JsonPropertyName("vehicleType")] string? VehicleType,
    [property: JsonPropertyName("baseFee")] long BaseFee,
    [property: JsonPropertyName("perKmFee")] long PerKmFee,
    [property: JsonPropertyName("perMinuteFee")] long PerMinuteFee,
    [property: JsonPropertyName("minFee")] long MinFee,
    [property: JsonPropertyName("maxFee")] long? MaxFee,
    [property: JsonPropertyName("activeFrom")] DateTimeOffset ActiveFrom,
    [property: JsonPropertyName("activeTo")] DateTimeOffset? ActiveTo,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("surgeMultiplier")] decimal SurgeMultiplier,
    [property: JsonPropertyName("status")] string Status);
