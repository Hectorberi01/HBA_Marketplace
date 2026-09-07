using System.Security.Cryptography;
using System.Text;

namespace HBA.Deliveries.Domain.Webhooks;

/// <summary>LA SIGNATURE DES WEBHOOKS.</summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-HBA-Signature";

    /// <summary>
    /// En-tête portant l'identifiant d'événement, pour la déduplication côté
    /// partenaire.
    /// </summary>
    public const string EventIdHeaderName = "X-HBA-Event-Id";

    /// <summary>En-tête portant le type d'événement, pour aiguiller sans lire le corps.</summary>
    public const string EventTypeHeaderName = "X-HBA-Event-Type";

    /// <summary>Construit l'en-tête de signature.</summary>
    /// <param name="payload">Le corps EXACT qui sera transmis, octet pour octet.</param>
    /// <param name="secret">Secret partagé avec le partenaire.</param>
    /// <param name="atUtc">Instant de la signature.</param>
    public static string Build(string payload, string secret, DateTime atUtc)
    {
        var timestamp = new DateTimeOffset(atUtc, TimeSpan.Zero).ToUnixTimeSeconds();

        return $"t={timestamp},v1={Compute(payload, secret, timestamp)}";
    }

    /// <summary>
    /// Le condensat lui-même. Exposé pour que les tests — et un partenaire qui
    /// débogue son intégration — puissent refaire exactement le calcul.
    /// </summary>
    public static string Compute(string payload, string secret, long timestamp)
    {
        // La chaîne signée sépare l'horodatage du corps par un POINT. Sans
        // séparateur, « 1723372800 » + « 42… » et « 172337280 » + « 042… »
        // produiraient la même chaîne, donc la même signature : deux appels
        // différents, indiscernables.
        var signed = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signed));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
