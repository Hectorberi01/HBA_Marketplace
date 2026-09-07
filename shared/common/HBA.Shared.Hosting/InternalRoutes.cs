namespace HBA.Shared.Hosting;

/// <summary>Protection des appels de service à service.</summary>
public static class InternalRoutes
{
    /// <summary>En-tête HTTP portant le secret partagé.</summary>
    public const string HeaderName = "X-Internal-Key";

    /// <summary>La même clé, en métadonnée gRPC.</summary>
    public const string MetadataKey = "x-internal-key";

    /// <summary>Comparaison à temps constant du secret présenté.</summary>
    public static bool SecretsMatch(string? presented, string expected)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var a = System.Text.Encoding.UTF8.GetBytes(presented);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);

        return a.Length == b.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>Secret partagé des appels entre services.</summary>
public sealed class InternalCallOptions
{
    public const string SectionName = "Internal";

    /// <summary>JAMAIS DANS UN appsettings VERSIONNÉ — uniquement `Internal__ApiKey`.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Nom de CET hôte, tel qu'il figure dans <see cref="Grpc.AutorisationsGrpc"/>
    /// .
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>JAMAIS VERSIONNÉE — PKCS#8 en base64, propre à CET hôte.</summary>
    public string? PrivateKey { get; init; }

    /// <summary>Registre `nom=base64;nom=base64` des clés PUBLIQUES, identique partout.</summary>
    public string? PublicKeys { get; init; }

    /// <summary>Accepte une identité NON SIGNÉE. Développement uniquement.</summary>
    public bool IdentitesNonSignees { get; init; }
}
