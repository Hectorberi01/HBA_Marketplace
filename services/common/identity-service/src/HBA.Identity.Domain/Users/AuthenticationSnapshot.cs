namespace HBA.Identity.Domain.Users;

/// <summary>
/// Le contexte d'une authentification : QUAND elle a eu lieu, et PAR QUELS MOYENS.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS « QUAND LE JETON A ÉTÉ ÉMIS ».
///
/// Un jeton d'accès est réémis toutes les quinze minutes ; l'authentification qui
/// l'a autorisé, elle, peut dater de la veille. Confondre les deux est exactement
/// l'erreur qui vide le step-up du §37 de son contenu : un client qui rafraîchit
/// régulièrement resterait éternellement « fraîchement authentifié », et
/// l'exigence de mot de passe récent sur un virement se contournerait en
/// attendant.
///
/// Cette valeur est donc portée par le JETON DE RAFRAÎCHISSEMENT, recopiée à
/// l'identique d'une rotation à l'autre, et ne bouge qu'à deux occasions : une
/// nouvelle connexion, ou un appel explicite à `POST /auth/reauthenticate`.
///
/// `Methods` SUIT RFC 8176, ET LES VALEURS NE S'INVENTENT PAS.
///
/// `pwd` pour un mot de passe, `otp` pour un code à usage unique, `mfa` pour
/// « plusieurs facteurs ont été employés ». Un client qui lit `amr` s'attend à
/// ces jetons-là ; y écrire `password` ou `2fa` produirait un claim que personne
/// ne sait interpréter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
