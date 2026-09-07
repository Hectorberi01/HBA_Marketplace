using System.Security.Claims;

namespace HBA.Shared.Hosting.Http;

/// <summary>LA RÉAUTHENTIFICATION RÉCENTE — LU DANS LE JETON, PAR CHAQUE SERVICE.</summary>
public static class StepUpAuthentication
{
    /// <summary>Claim OIDC standard : instant de l'authentification, en secondes epoch.</summary>
    public const string AuthTimeClaim = "auth_time";

    /// <summary>
    /// Claim OIDC standard (RFC 8176) : méthodes employées — `pwd`, `otp`, `mfa`.
    /// </summary>
    public const string AuthMethodsClaim = "amr";

    /// <summary>La valeur `amr` du mot de passe (RFC 8176 §2).</summary>
    public const string PasswordMethod = "pwd";

    /// <summary>La fenêtre pendant laquelle une authentification reste « récente ».</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>L'instant d'authentification porté par le jeton, ou <c>null</c>.</summary>
    public static DateTimeOffset? AuthenticatedAt(this ClaimsPrincipal user)
    {
        var brut = user.FindFirstValue(AuthTimeClaim);

        return long.TryParse(brut, out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;
    }

    /// <summary>Les méthodes d'authentification employées, éventuellement vides.</summary>
    public static IReadOnlyList<string> AuthMethods(this ClaimsPrincipal user)
        => [.. user.FindAll(AuthMethodsClaim).Select(c => c.Value)];

    /// <summary>
    /// Le porteur a-t-il saisi son MOT DE PASSE il y a moins de
    /// <see cref="Window"/> ?
    /// </summary>
    public static bool HasRecentAuthentication(this ClaimsPrincipal user, DateTimeOffset? nowUtc = null)
    {
        if (user.AuthenticatedAt() is not { } authentifieLe)
        {
            return false;
        }

        // UN `amr` ABSENT EST REFUSÉ, comme un `auth_time` absent — même
        // raisonnement, même conséquence bénigne : `JwtTokenGenerator` pose
        // toujours ce claim depuis le lot 0b, donc l'absence désigne un jeton
        // d'avant, qui expire de lui-même en quelques minutes.
        if (!user.AuthMethods().Contains(PasswordMethod, StringComparer.Ordinal))
        {
            return false;
        }

        var maintenant = nowUtc ?? DateTimeOffset.UtcNow;
        var ecart = maintenant - authentifieLe;

        return ecart >= -TimeSpan.FromMinutes(1) && ecart <= Window;
    }
}
