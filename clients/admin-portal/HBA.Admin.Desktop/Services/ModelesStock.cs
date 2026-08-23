using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Un article de stock sous seuil, `InventoryItemSummary`.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `Available` N'EST PAS `OnHand` : LA DIFFÉRENCE EST CE QUI EST RÉSERVÉ.
///
/// `OnHand` est ce qui est physiquement là ; `Reserved` est ce que des commandes
/// en cours ont déjà pris ; `Available` est ce qui reste vendable. Un article
/// peut donc être « en rupture » avec des cartons dans l'entrepôt — et c'est
/// exactement la situation qu'un gestionnaire doit distinguer d'une vraie
/// rupture, parce qu'elle se règle autrement.
///
/// `IsLowStock` est calculé côté serveur contre `ReorderThreshold` : l'écran ne
/// le recalcule pas, il l'affiche.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ArticleStock(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("locationId")] Guid LocationId,
    [property: JsonPropertyName("onHand")] int OnHand,
    [property: JsonPropertyName("reserved")] int Reserved,
    [property: JsonPropertyName("available")] int Available,
    [property: JsonPropertyName("reorderThreshold")] int ReorderThreshold,
    [property: JsonPropertyName("isLowStock")] bool IsLowStock);

/// <summary>Un lieu d'expédition, `FulfillmentLocationSummary`.</summary>
/// <param name="OwnerId">
/// Le vendeur propriétaire — nul pour un entrepôt de la plateforme. C'est ce qui
/// distingue les deux natures de lieu dans une liste qui les mélange.
/// </param>
/// <param name="ContactPhone">
/// « Le numéro à composer sur place […] le champ le plus rentable du
/// formulaire », dit le contrat : c'est ce que le livreur utilise quand il ne
/// trouve pas la boutique.
/// </param>
public sealed record LieuStock(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("ownerId")] Guid? OwnerId,
    [property: JsonPropertyName("communeCode")] string CommuneCode,
    [property: JsonPropertyName("communeName")] string CommuneName,
    [property: JsonPropertyName("quartier")] string? Quartier,
    [property: JsonPropertyName("landmark")] string? Landmark,
    [property: JsonPropertyName("line")] string? Line,
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("contactPhone")] string? ContactPhone);
