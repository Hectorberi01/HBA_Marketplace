using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

/// <summary>Un compte, tel que `UserSummary` le rend.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `EmailVerifiedByAdminOnUtc` N'EST PAS UN DOUBLON DE `EmailVerified`.
///
/// Le contrat le dit : « renseignée = l'e-mail a été marqué vérifié PAR UN
/// ADMINISTRATEUR, sur attestation, et non par le titulaire cliquant un lien. La
/// console doit pouvoir le montrer : "Oui" et "Oui, sur parole" ne valent pas la
/// même chose. » L'écran distingue donc les deux.
///
/// `RoleIds` NE PORTE QUE DES IDENTIFIANTS. Les noms se résolvent contre la
/// liste des rôles, chargée séparément — c'est le même service, mais deux
/// routes, et aucune ne les joint.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record CompteAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("firstName")] string FirstName,
    [property: JsonPropertyName("lastName")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("emailVerified")] bool EmailVerified,
    [property: JsonPropertyName("mfaEnabled")] bool MfaEnabled,
    [property: JsonPropertyName("roleIds")] IReadOnlyList<Guid>? RoleIds,
    [property: JsonPropertyName("acceptedTermsVersion")] string? AcceptedTermsVersion,
    [property: JsonPropertyName("acceptedTermsOnUtc")] DateTime? AcceptedTermsOnUtc,
    [property: JsonPropertyName("emailVerifiedByAdminOnUtc")] DateTime? EmailVerifiedByAdminOnUtc);
