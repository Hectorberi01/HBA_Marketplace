namespace HBA.Gateway.Infrastructure.Authentication;

/// <summary>
/// Liste blanche des en-têtes recopiés de la requête entrante vers les appels
/// sortants de la passerelle.
/// </summary>
public static class OutboundHeaderPolicy
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LISTE BLANCHE, JAMAIS LISTE NOIRE.
    ///
    /// Une liste noire oblige à prévoir tout ce qui est dangereux. Il suffit qu'un
    /// service adopte demain un en-tête de confiance — `X-Internal-Actor`,
    /// `X-Admin-Override` — pour qu'un client puisse l'envoyer à la passerelle et
    /// se le faire relayer, signé de la crédibilité du réseau interne. La liste
    /// blanche rend cet oubli impossible : ce qui n'est pas nommé ici ne part pas.
    ///
    /// `Cookie` est absent volontairement : la passerelle authentifie par jeton
    /// porteur. Relayer les cookies ferait circuler des sessions de navigateur
    /// jusqu'à des services qui n'en attendent pas et qui, un jour, s'en
    /// serviraient.
    ///
    /// `Host` est absent : il doit être celui du service appelé, pas celui du
    /// domaine public — sinon le service qui construit des URL absolues à partir
    /// de `Host` produirait des liens pointant vers lui-même via Internet.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static readonly string[] Allowed =
    [
        // Identité de l'appelant : le service refait ses propres contrôles.
        "Authorization",

        // Corrélation applicative et trace W3C — sans elles, la trace distribuée
        // s'arrête à la passerelle et le lien client → service est perdu.
        "X-Correlation-ID",
        "traceparent",
        "tracestate",
        "baggage",

        // Idempotence : c'est le client qui la fournit et le service qui l'honore.
        // La passerelle ne fait que la transporter — mais si elle la perdait, la
        // protection contre le double paiement disparaîtrait sans un seul message
        // d'erreur.
        "Idempotency-Key",

        // Langue de réponse (messages d'erreur, libellés).
        "Accept-Language"
    ];

    /// <summary>
    /// En-têtes RETIRÉS de la requête entrante avant de l'envoyer au service.
    /// </summary>
    /// <remarks>
    /// CES EN-TÊTES SONT CEUX QUE LES SERVICES POURRAIENT CROIRE INTERNES.
    ///
    /// Un service qui lit `X-User-Id` en le supposant posé par la passerelle
    /// accorderait l'identité de n'importe qui à quiconque l'envoie depuis
    /// Internet. Tant que la passerelle ne les pose pas elle-même, elle doit les
    /// effacer — le coût est nul, l'oubli est une élévation de privilège.
    /// </remarks>
    public static readonly string[] StrippedFromInbound =
    [
        "X-User-Id",
        "X-User-Roles",
        "X-Internal-Call",
        "X-Gateway-Bypass"
    ];
}
