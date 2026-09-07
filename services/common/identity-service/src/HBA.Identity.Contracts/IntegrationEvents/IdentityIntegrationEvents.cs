using HBA.Shared.IntegrationEvents;

namespace HBA.Identity.Contracts.IntegrationEvents;

/// <summary>
/// Un compte a été créé. Consommé par Notifications (bienvenue), Analytics, et par
/// user-service qui en crée le profil.
/// </summary>
[HbaEvent("identity", "user", "registered", Version = 1, AggregateType = "User")]
public sealed record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>Null pour les messages publiés avant l'ajout du champ.</summary>
    public string? LastName { get; init; }
}

/// <summary>Une vérification d'e-mail est demandée.</summary>
[HbaEvent("identity.email.verification.requested")]
public sealed record EmailVerificationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>
    /// Code chiffré. Déchiffrable uniquement avec `Security:SecretProtection:Key`.
    /// </summary>
    public required string ProtectedVerificationToken { get; init; }
}

/// <summary>
/// Un code à usage unique vient d'être émis et doit être REMIS à son destinataire.
/// </summary>
[HbaEvent("identity", "otp", "issued", Version = 1, AggregateType = "User")]
public sealed record OtpChallengeIssuedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>`SMS` ou `EMAIL` — voir `MfaChannels`.</summary>
    public required string Channel { get; init; }

    public required string Email { get; init; }

    /// <summary>Au format international, `+229` suivi de dix chiffres.</summary>
    public required string PhoneNumber { get; init; }

    public required string FirstName { get; init; }

    /// <summary>
    /// Code chiffré. Déchiffrable uniquement avec `Security:SecretProtection:Key`.
    /// </summary>
    public required string ProtectedCode { get; init; }

    /// <summary>
    /// Permet au message de dire « valable dix minutes » sans que le gabarit ait à
    /// connaître `MfaChallenge.Lifetime` — donc sans qu'un changement de durée
    /// laisse un message qui ment.
    /// </summary>
    public required DateTime ExpiresAtUtc { get; init; }
}

/// <summary>L'e-mail d'un compte a été confirmé.</summary>
[HbaEvent("identity.user.email.confirmed")]
public sealed record UserEmailConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
}

/// <summary>Le nom d'un compte a changé.</summary>
[HbaEvent("identity.user.profile.updated")]
public sealed record UserProfileUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}

/// <summary>Un compte a été anonymisé à la demande de son titulaire.</summary>
[HbaEvent("identity.user.anonymized")]
public sealed record UserAnonymizedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
}

/// <summary>Une réinitialisation de mot de passe est demandée.</summary>
[HbaEvent("identity.password.reset.requested")]
public sealed record PasswordResetRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }

    /// <summary>
    /// Code chiffré. Déchiffrable uniquement avec `Security:SecretProtection:Key`.
    /// </summary>
    public required string ProtectedResetToken { get; init; }
}


// LES DEUX ÉVÉNEMENTS MANQUANTS DU §10.1.

/// <summary>Une connexion a réussi.</summary>
[HbaEvent("identity", "user", "logged_in", Version = 1, AggregateType = "User")]
public sealed record UserLoggedInIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>`PASSWORD`, `REFRESH`, `OTP` — comment la session a été obtenue.</summary>
    public required string Method { get; init; }

    /// <summary>Identifiant d'appareil fourni par le client, ou null.</summary>
    public string? DeviceId { get; init; }
}

/// <summary>Des sessions ont été révoquées.</summary>
[HbaEvent("identity", "token", "revoked", Version = 1, AggregateType = "User")]
public sealed record TokenRevokedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>`LOGOUT`, `ADMIN_REVOKE`, `PASSWORD_CHANGED`, `SUSPENDED`.</summary>
    public required string Reason { get; init; }

    /// <summary>Nombre de jetons de rafraîchissement révoqués.</summary>
    public required int RevokedCount { get; init; }
}
