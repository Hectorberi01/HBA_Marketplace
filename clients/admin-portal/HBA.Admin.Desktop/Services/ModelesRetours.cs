using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

// ═════════════════════════════════════════════════════════════════════════════
// LES DOSSIERS DE RETOUR (return-refund-service).
//
// LES ÉNUMÉRATIONS ARRIVENT EN NOMBRES, PAS EN CHAÎNES.
//
// `ReturnRequestDto` porte les types d'énumération DIRECTEMENT — `ReturnStatus
// Status`, et non `string Status` comme ailleurs. Aucun `JsonStringEnumConverter`
// n'est enregistré dans le dépôt : System.Text.Json sérialise donc ces champs en
// ENTIERS.
//
// CONSÉQUENCE, ET C'EST UNE INCOHÉRENCE DE L'API ELLE-MÊME : le filtre de la
// route se passe par NOM (`?status=ManualReview`, lu par `Enum.TryParse`), et la
// réponse rend un NUMÉRO. Le client envoie donc un mot et relit un chiffre.
//
// Lire `Status` comme une chaîne donnerait une exception de désérialisation, et
// `Lire<T>` la transforme en `null` : la liste s'afficherait vide, sans erreur.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un montant, `MoneyDto`.</summary>
public sealed record MontantApi(
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency);

/// <summary>Un dossier de retour, `ReturnRequestDto`.</summary>
/// <remarks>
/// `ResolutionRequested` N'EST PAS REPRIS ICI, ET C'EST DÉLIBÉRÉ.
///
/// Le domaine le dit de lui-même : « aucune des cinq valeurs n'est jamais posée
/// […] la résolution d'un retour n'est décidée nulle part ». Le champ vaut donc
/// toujours 0 — `Refund`. L'afficher laisserait croire à une décision qui n'a
/// jamais été prise, sur un écran où l'on décide précisément de cela.
/// </remarks>
public sealed record DossierRetour(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("returnNumber")] string ReturnNumber,
    [property: JsonPropertyName("orderId")] Guid OrderId,
    [property: JsonPropertyName("customerId")] Guid CustomerId,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("reasonCode")] int ReasonCode,
    [property: JsonPropertyName("estimatedRefund")] MontantApi? EstimatedRefund,
    [property: JsonPropertyName("approvedRefund")] MontantApi? ApprovedRefund,
    [property: JsonPropertyName("returnShippingPayer")] string? ReturnShippingPayer,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("expiresAtUtc")] DateTime ExpiresAtUtc,
    [property: JsonPropertyName("resolvedAtUtc")] DateTime? ResolvedAtUtc,
    [property: JsonPropertyName("items")] IReadOnlyList<LigneRetourApi>? Items);

/// <summary>Une ligne d'un dossier de retour, `ReturnItemDto`.</summary>
public sealed record LigneRetourApi(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("skuSnapshot")] string SkuSnapshot,
    [property: JsonPropertyName("nameSnapshot")] string NameSnapshot,
    [property: JsonPropertyName("requestedQuantity")] int RequestedQuantity,
    [property: JsonPropertyName("receivedQuantity")] int ReceivedQuantity,
    [property: JsonPropertyName("unitPaid")] MontantApi? UnitPaid);
