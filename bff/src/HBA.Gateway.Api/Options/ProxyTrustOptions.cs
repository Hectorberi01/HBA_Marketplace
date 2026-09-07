namespace HBA.Gateway.Api.Options;

/// <summary>
/// Détermine si les en-têtes <c> X-Forwarded-*</c> reçus sont dignes de confiance.
/// </summary>
public sealed class ProxyTrustOptions
{
    public const string SectionName = "ProxyTrust";

    /// <summary>Adresses des proxys inverses autorisés à poser <c>X-Forwarded-For</c>.</summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>Fait confiance à TOUT appelant pour les en-têtes <c>X-Forwarded-*</c>.</summary>
    public bool TrustAnyProxy { get; init; }
}
