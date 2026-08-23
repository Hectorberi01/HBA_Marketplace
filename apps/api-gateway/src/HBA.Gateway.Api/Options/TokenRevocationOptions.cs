namespace HBA.Gateway.Api.Options;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// VÉRIFICATION DE RÉVOCATION DES JETONS — RÉGLAGES (ISSUE-022, décision D27).
///
/// CE QUE CE CONTRÔLE RÉPARE.
///
/// `IdentityModuleApi.ValidateAccessTokenAsync` compare le `security_stamp` porté
/// par le jeton à celui du compte — exactement le contrôle qui permet de refuser
/// un jeton cryptographiquement valide mais métier-mort. Elle était écrite,
/// complète, et n'avait AUCUN appelant. Déconnexion, changement de mot de passe et
/// suspension n'invalidaient donc rien : le jeton restait bon jusqu'à son
/// expiration naturelle, quinze minutes. Le mécanisme de révocation existait et ne
/// servait à rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class TokenRevocationOptions
{
    public const string SectionName = "TokenRevocation";

    /// <summary>
    /// NE PAS METTRE À `false` POUR « DÉBOGUER ».
    ///
    /// Ce drapeau existe pour un environnement de test qui n'a pas
    /// d'identity-service — pas pour contourner un incident. Désactivé, la
    /// plateforme retrouve exactement le défaut ISSUE-022, en silence et en
    /// permanence. Pour une panne d'identity, l'échec ouvert ci-dessous suffit :
    /// il est temporaire et il est BRUYANT.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Durée de mémorisation d'un verdict rendu par identity.
    ///
    /// C'EST LE DÉLAI RÉEL DE PRISE D'EFFET D'UNE RÉVOCATION, et c'est le seul
    /// arbitrage qui compte ici. Trente secondes ramènent le sursis de quinze
    /// minutes à une demi-minute. L'allonger à cinq minutes rendrait le contrôle
    /// presque décoratif — ce qu'il était déjà ; le réduire à zéro ferait
    /// d'identity une dépendance de CHAQUE requête de la plateforme, ce que D27 a
    /// explicitement refusé.
    /// </summary>
    public int CacheSeconds { get; init; } = 30;

    /// <summary>
    /// Durée de mémorisation d'un ÉCHEC (identity injoignable).
    ///
    /// VOLONTAIREMENT BEAUCOUP PLUS COURTE que <see cref="CacheSeconds"/>.
    ///
    /// Sans elle, une panne d'identity ferait partir un appel gRPC — et donc un
    /// délai d'attente — à chaque requête de la plateforme : la panne d'un service
    /// deviendrait une lenteur générale. Avec elle, le trafic d'une même session
    /// n'en paie qu'un toutes les N secondes. Elle reste courte parce que ces
    /// secondes-là sont du temps pendant lequel un compte suspendu garde ses
    /// droits.
    /// </summary>
    public int FailOpenCacheSeconds { get; init; } = 5;

    /// <summary>
    /// Délai d'attente de l'appel à identity.
    ///
    /// SANS LUI, L'ÉCHEC OUVERT NE PROTÈGE DE RIEN. Un identity qui répond en
    /// trente secondes n'est pas « indisponible » du point de vue du client gRPC :
    /// aucune exception n'est levée, la requête attend. La passerelle ralentirait
    /// donc au rythme d'identity, ce que la décision d'échouer ouvert visait
    /// précisément à éviter.
    /// </summary>
    public int TimeoutMilliseconds { get; init; } = 1500;
}
