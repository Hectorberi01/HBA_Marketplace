namespace HBA.Identity.Contracts;

/// <summary>Vue publique d'un compte, exposée aux autres modules et au front.</summary>
public sealed record UserSummary(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Status,
    bool EmailVerified,
    bool MfaEnabled,
    IReadOnlyList<Guid> RoleIds,
    // Version des conditions générales acceptée par ce compte, et date.
    string? AcceptedTermsVersion = null,
    DateTime? AcceptedTermsOnUtc = null,
    // Renseignée = l'e-mail a été marqué vérifié PAR UN ADMINISTRATEUR, sur
    // attestation, et non par le titulaire cliquant un lien.
    DateTime? EmailVerifiedByAdminOnUtc = null);
