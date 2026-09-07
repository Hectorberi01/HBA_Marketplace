namespace HBA.Identity.Domain.Users;

/// <summary>
/// Le contexte d'une authentification : QUAND elle a eu lieu, et PAR QUELS MOYENS.
/// </summary>
public readonly record struct AuthenticationSnapshot(DateTime AuthenticatedAtUtc, string Methods)
{
    /// <summary>Mot de passe seul.</summary>
    public const string Password = "pwd";

    /// <summary>Code à usage unique.</summary>
    public const string OneTimeCode = "otp";

    /// <summary>Marqueur « plusieurs facteurs », posé en plus des méthodes précises.</summary>
    public const string MultiFactor = "mfa";

    /// <summary>Une authentification par mot de passe, à l'instant donné.</summary>
    public static AuthenticationSnapshot ByPassword(DateTime nowUtc)
        => new(nowUtc, Password);

    /// <summary>Une authentification par mot de passe ET second facteur.</summary>
    public static AuthenticationSnapshot ByPasswordAndOtp(DateTime nowUtc)
        => new(nowUtc, $"{Password} {OneTimeCode} {MultiFactor}");

    /// <summary>Les méthodes, éclatées — un claim `amr` par valeur.</summary>
    public IReadOnlyList<string> MethodList()
        => Methods.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
