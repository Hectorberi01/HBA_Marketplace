namespace HBA.Gateway.Infrastructure.Authentication;

/// <summary>
/// Liste blanche des en-têtes recopiés de la requête entrante vers les appels
/// sortants de la passerelle.
/// </summary>
public static class OutboundHeaderPolicy
{
    /// <summary>LISTE BLANCHE, JAMAIS LISTE NOIRE.</summary>
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
        "Idempotency-Key",

        // Langue de réponse (messages d'erreur, libellés).
        "Accept-Language"
    ];

    /// <summary>En-têtes RETIRÉS de la requête entrante avant de l'envoyer au service.</summary>
    public static readonly string[] StrippedFromInbound =
    [
        "X-User-Id",
        "X-User-Roles",
        "X-Internal-Call",
        "X-Gateway-Bypass"
    ];
}
