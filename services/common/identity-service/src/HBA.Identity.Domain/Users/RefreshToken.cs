using HBA.Shared.Domain.Primitives;

namespace HBA.Identity.Domain.Users;

/// <summary>Jeton de rafraîchissement.</summary>
public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken()
    {
    }

    internal RefreshToken(
        Guid id, string tokenHash, DateTime expiresOnUtc,
        DateTime authenticatedAtUtc, string authMethods)
        : base(id)
    {
        TokenHash = tokenHash;
        ExpiresOnUtc = expiresOnUtc;
        CreatedOnUtc = DateTime.UtcNow;
        AuthenticatedAtUtc = authenticatedAtUtc;
        AuthMethods = authMethods;
    }

    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? RevokedOnUtc { get; private set; }

    /// <summary>
    /// L'instant où le titulaire a RÉELLEMENT prouvé son identité — pas celui où ce
    /// jeton a été créé.
    /// </summary>
    public DateTime AuthenticatedAtUtc { get; private set; }

    /// <summary>
    /// Les méthodes employées à cette authentification, séparées par des espaces —
    /// `pwd`, `pwd otp`.
    /// </summary>
    public string AuthMethods { get; private set; } = default!;

    public bool IsActive(DateTime nowUtc) => RevokedOnUtc is null && ExpiresOnUtc > nowUtc;

    internal void Revoke() => RevokedOnUtc = DateTime.UtcNow;
}
