namespace HBA.Gateway.Api.Options;

/// <summary>
/// Détermine si les en-têtes <c>X-Forwarded-*</c> reçus sont dignes de confiance.
/// </summary>
public sealed class ProxyTrustOptions
{
    public const string SectionName = "ProxyTrust";

    /// <summary>
    /// Adresses des proxys inverses autorisés à poser <c>X-Forwarded-For</c>.
    /// </summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>
    /// Fait confiance à TOUT appelant pour les en-têtes <c>X-Forwarded-*</c>.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE NOM EST LONG ET DÉSAGRÉABLE EXPRÈS.
    ///
    /// À `true`, n'importe quel appelant peut se déclarer venir de l'adresse de
    /// son choix. Comme la limitation de débit partitionne par IP pour le trafic
    /// ANONYME, cela revient à supprimer la protection de `/api/auth/login` :
    /// une nouvelle valeur d'en-tête à chaque requête donne une partition neuve à
    /// chaque requête, et l'énumération de mots de passe redevient libre.
    ///
    /// Ce n'est acceptable QUE si la passerelle est strictement injoignable
    /// autrement que par Traefik — c'est le cas dans `compose.gateway.yml`, où
    /// aucun port n'est publié et où seul le réseau `hba-proxy` y accède. Cette
    /// garantie tient à la configuration Docker : elle disparaît le jour où
    /// quelqu'un ajoute `ports: - "8080:8080"` pour déboguer.
    ///
    /// Préférer `KnownProxies` dès que l'adresse de Traefik est stable.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public bool TrustAnyProxy { get; init; }
}
