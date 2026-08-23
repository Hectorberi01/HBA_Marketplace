using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Un avis, tel que `ReviewSummary` le rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ICI `Status` EST UNE CHAÎNE, CONTRAIREMENT AUX DOSSIERS DE RETOUR.
///
/// `ReviewSummary` porte `string Status`, projeté par `ReviewMapper.ToSummary`
/// avec un `.ToString()`. `ReturnRequestDto`, lui, porte l'énumération elle-même
/// et arrive en entier. Deux services, deux conventions, dans la même console :
/// c'est l'endpoint qui tranche, pas l'habitude.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record AvisAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("buyerId")] Guid BuyerId,
    [property: JsonPropertyName("rating")] int Rating,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("isVerifiedPurchase")] bool IsVerifiedPurchase,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("sellerReply")] string? SellerReply,
    [property: JsonPropertyName("sellerRepliedAtUtc")] DateTime? SellerRepliedAtUtc);
