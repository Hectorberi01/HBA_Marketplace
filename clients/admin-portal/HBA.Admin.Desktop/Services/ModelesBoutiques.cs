using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Une boutique, telle que `StoreSummary` la rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN VENDEUR N'EST PAS UNE BOUTIQUE, ET C'EST TOUT L'OBJET DE CE PANNEAU.
///
/// Un vendeur peut en tenir plusieurs. Jusqu'ici la console ne savait agir que
/// sur le vendeur entier — c'est-à-dire sanctionner trois boutiques pour ce
/// qu'une seule a fait.
///
/// PAS D'ADRESSE DANS CE CONTRAT, ET LE COMMENTAIRE DU DÉPÔT DIT POURQUOI.
///
/// « Le lieu physique vit dans Inventory (`FulfillmentLocation`) et n'est
/// référencé que par son identifiant : recopier l'adresse créerait deux vérités
/// pour un même lieu, qui divergeraient au premier déménagement. » L'écran Stock
/// porte déjà ces lieux ; c'est là qu'on lit une adresse, pas ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="IsSelling">
/// Ses offres sont-elles achetables MAINTENANT. Distinct du statut : une boutique
/// ouverte hors de ses horaires ne vend pas non plus.
/// </param>
/// <param name="StatusReason">
/// Le motif de la fermeture ou de la suspension. Absent de la vitrine publique —
/// « il peut mentionner une sanction ».
/// </param>
public sealed record BoutiqueAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("logoUrl")] string? LogoUrl,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("contactPhone")] string ContactPhone,
    [property: JsonPropertyName("contactEmail")] string? ContactEmail,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isSelling")] bool IsSelling,
    [property: JsonPropertyName("fulfillmentLocationId")] Guid? FulfillmentLocationId,
    [property: JsonPropertyName("statusReason")] string? StatusReason,
    [property: JsonPropertyName("createdOnUtc")] DateTime CreatedOnUtc);
