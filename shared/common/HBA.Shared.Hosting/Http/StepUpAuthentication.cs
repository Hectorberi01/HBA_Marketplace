using System.Security.Claims;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉAUTHENTIFICATION RÉCENTE — LU DANS LE JETON, PAR CHAQUE SERVICE.
///
/// POURQUOI CE CONTRÔLE NE PASSE PAR AUCUN APPEL RÉSEAU.
///
/// « Ce compte a-t-il saisi son mot de passe il y a moins de cinq minutes » est
/// une propriété du JETON, pas du dossier vendeur. Le demander à
/// merchant-service par gRPC serait absurde : merchant-service devrait lui-même
/// interroger identity, qui devrait tenir un registre des sessions — trois sauts
/// pour relire une valeur que l'appelant présente déjà, signée.
///
/// `auth_time` est un claim OIDC standard (RFC 8176 §2 pour `amr`,
/// OpenID Connect Core §2 pour `auth_time`) : l'instant de l'authentification
/// EFFECTIVE, en secondes epoch. Il n'est PAS l'instant d'émission du jeton.
///
/// LA DISTINCTION `auth_time` / `iat` EST TOUT LE MÉCANISME.
///
/// Un jeton rafraîchi porte un `iat` neuf — c'est sa raison d'être. S'il portait
/// aussi un `auth_time` neuf, un client qui rafraîchit toutes les quatre minutes
/// resterait éternellement « fraîchement authentifié » et le step-up ne
/// vaudrait rien. `RefreshTokenCommandHandler` recopie donc l'`auth_time` de la
/// session d'origine, conservé sur le jeton de rafraîchissement lui-même.
///
/// CE QUI SE PASSE QUAND LE CLAIM EST ABSENT.
///
/// Refus. Les jetons émis avant le lot 0b n'ont pas d'`auth_time` : les traiter
/// comme « authentification inconnue donc acceptée » ouvrirait le contournement
/// le plus simple qui soit — présenter un vieux jeton. Ils expirent d'eux-mêmes
/// en quelques minutes, et l'utilisateur qui en porte un est renvoyé vers la
/// saisie de mot de passe : le pire qu'il subisse est une saisie de trop.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class StepUpAuthentication
{
    /// <summary>Claim OIDC standard : instant de l'authentification, en secondes epoch.</summary>
    public const string AuthTimeClaim = "auth_time";

    /// <summary>Claim OIDC standard (RFC 8176) : méthodes employées — `pwd`, `otp`, `mfa`.</summary>
    public const string AuthMethodsClaim = "amr";

    /// <summary>
    /// La valeur `amr` du mot de passe (RFC 8176 §2). C'est ELLE que le step-up exige.
    /// </summary>
    /// <remarks>
    /// LA VALEUR NE S'INVENTE PAS. `pwd`, pas `password` ni `pass` — elle est posée
    /// par <c>AuthenticationSnapshot.Password</c> côté identity, et les deux doivent
    /// rester d'accord. Une divergence ne casserait rien au démarrage : elle ferait
    /// simplement refuser TOUS les gestes sensibles, à tout le monde, sans message
    /// explicatif.
    /// </remarks>
    public const string PasswordMethod = "pwd";

    /// <summary>
    /// La fenêtre pendant laquelle une authentification reste « récente ».
    /// </summary>
    /// <remarks>
    /// CINQ MINUTES, ET LE CHIFFRE N'EST PAS ARBITRAIRE.
    ///
    /// Assez long pour qu'un vendeur qui vient de se connecter enchaîne sur un
    /// virement sans ressaisir son mot de passe ; assez court pour qu'un poste
    /// laissé ouvert au marché ne serve pas à vider un portefeuille une heure
    /// plus tard. C'est aussi l'ordre de grandeur retenu par les places de
    /// marché qui documentent leur step-up.
    ///
    /// Elle n'est PAS configurable par service : deux services qui n'auraient pas
    /// la même fenêtre laisseraient le geste le plus dangereux passer par celui
    /// qui est le plus laxiste.
    /// </remarks>
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
    /// Le porteur a-t-il saisi son MOT DE PASSE il y a moins de <see cref="Window"/> ?
    /// </summary>
    /// <remarks>
    /// UN `auth_time` DANS LE FUTUR EST REFUSÉ, PAS ACCEPTÉ.
    ///
    /// Une horloge d'émetteur en avance rendrait « récent » un jeton indéfiniment.
    /// La tolérance d'une minute absorbe la dérive normale entre machines ; au-delà,
    /// le refus est le bon comportement — c'est un symptôme, pas un cas nominal.
    /// <para>
    /// LA MÉTHODE EST VÉRIFIÉE DEPUIS L'OUVERTURE DE LA CONNEXION PAR OTP.
    ///
    /// Ce contrôle ne lisait QUE `auth_time`. L'encadré en tête de ce fichier disait
    /// pourtant, depuis le premier jour, ce qu'il était censé répondre : « ce compte
    /// a-t-il saisi son MOT DE PASSE il y a moins de cinq minutes ». Il ne le
    /// vérifiait pas — et c'était sans conséquence tant que tout chemin d'émission
    /// de jetons passait par un mot de passe : `ByPassword` et `ByPasswordAndOtp`
    /// portent l'une comme l'autre `pwd`.
    ///
    /// ISSUE-062 a ouvert un second chemin d'entrée : `POST /auth/verify-otp` émet
    /// des jetons SANS mot de passe, avec `amr = otp` seul. Sans cette ligne, qui
    /// reçoit un SMS obtiendrait aussitôt un jeton « fraîchement authentifié » — et
    /// franchirait les six gardes de step-up du dépôt : virement, compte bancaire,
    /// transfert de propriété vendeur, mouvements de stock. Une carte SIM
    /// suffirait à vider un portefeuille.
    ///
    /// L'hypothèse était juste, elle n'était simplement écrite nulle part dans le
    /// code. Elle l'est maintenant.
    /// </para>
    /// <para>
    /// CE QUE CE CONTRÔLE NE FAIT PAS : dire à l'appelant POURQUOI il refuse. Un
    /// jeton trop vieux et un jeton sans mot de passe rendent le même `false`, donc
    /// la même invitation à se réauthentifier — et `POST /auth/reauthenticate`
    /// résout les deux cas de la même façon, puisqu'il exige le mot de passe. Si un
    /// jour un message distinct est nécessaire (« ce geste demande votre mot de
    /// passe »), c'est un second prédicat qu'il faudra, pas un assouplissement de
    /// celui-ci.
    /// </para>
    /// </remarks>
    public static bool HasRecentAuthentication(this ClaimsPrincipal user, DateTimeOffset? nowUtc = null)
    {
        if (user.AuthenticatedAt() is not { } authentifieLe)
        {
            return false;
        }

        // UN `amr` ABSENT EST REFUSÉ, comme un `auth_time` absent — même
        // raisonnement, même conséquence bénigne : `JwtTokenGenerator` pose toujours
        // ce claim depuis le lot 0b, donc l'absence désigne un jeton d'avant, qui
        // expire de lui-même en quelques minutes. Le traiter comme « méthode inconnue
        // donc acceptée » offrirait le contournement le plus simple qui soit.
        if (!user.AuthMethods().Contains(PasswordMethod, StringComparer.Ordinal))
        {
            return false;
        }

        var maintenant = nowUtc ?? DateTimeOffset.UtcNow;
        var ecart = maintenant - authentifieLe;

        return ecart >= -TimeSpan.FromMinutes(1) && ecart <= Window;
    }
}
