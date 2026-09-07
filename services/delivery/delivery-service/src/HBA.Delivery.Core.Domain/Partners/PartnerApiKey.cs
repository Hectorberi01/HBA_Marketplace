using System.Security.Cryptography;
using System.Text;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Domain.Partners;

/// <summary>Identité forte d'un partenaire.</summary>
public readonly record struct PartnerId(Guid Value)
{
    public static PartnerId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Clé fraîchement émise : la seule fois où le secret est lisible.</summary>
/// <param name="Key">Clé complète, à remettre au partenaire et à ne jamais journaliser.</param>
/// <param name="Prefix">Partie publique, conservée pour identifier la clé sans la révéler.</param>
public readonly record struct IssuedApiKey(string Key, string Prefix);

/// <summary>UNE CLÉ D'API — STOCKÉE COMME UN MOT DE PASSE, JAMAIS EN CLAIR.</summary>
public sealed class PartnerApiKey : Entity<Guid>
{
    /// <summary>Longueur du préfixe public.</summary>
    private const int PrefixLength = 12;

    /// <summary>Octets d'aléa du secret. 32 octets = 256 bits.</summary>
    private const int SecretBytes = 32;

    private PartnerApiKey(Guid id, string prefix, string hash, string? label)
        : base(id)
    {
        Prefix = prefix;
        Hash = hash;
        Label = label;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private PartnerApiKey()
    {
        Prefix = string.Empty;
        Hash = string.Empty;
    }

    /// <summary>Partie publique, indexée.</summary>
    public string Prefix { get; private set; }

    /// <summary>Condensat SHA-256 de la clé complète, en base64.</summary>
    public string Hash { get; private set; }

    /// <summary>Étiquette libre : « intégration boutique X », « test ».</summary>
    public string? Label { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>
    /// Dernier usage constaté. Sert à repérer les clés dormantes — celles qu'on
    /// peut révoquer sans rien casser, et qui sont pourtant la première porte
    /// d'entrée d'une fuite ancienne.
    /// </summary>
    public DateTime? LastUsedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    /// <summary>
    /// Émet une clé. Le secret en clair n'est renvoyé QU'ICI : il n'est stocké
    /// nulle part et ne pourra jamais être retrouvé.
    /// </summary>
    internal static (PartnerApiKey Key, IssuedApiKey Issued) Issue(string environmentTag, string? label)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty);

        var prefix = secret[..PrefixLength];
        var full = $"hba_{environmentTag}_{prefix}_{secret[PrefixLength..]}";

        var key = new PartnerApiKey(Guid.NewGuid(), prefix, ComputeHash(full), label);
        return (key, new IssuedApiKey(full, prefix));
    }

    /// <summary>Vérifie une clé présentée, à temps constant.</summary>
    public bool Matches(string candidate)
    {
        if (!IsActive)
        {
            return false;
        }

        var expected = Convert.FromBase64String(Hash);
        var actual = Convert.FromBase64String(ComputeHash(candidate));

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;

    /// <summary>
    /// Note l'usage. Volontairement APPROXIMATIF : on n'écrit qu'une fois par
    /// heure.
    /// </summary>
    public bool TouchIfStale()
    {
        if (LastUsedAtUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(1))
        {
            return false;
        }

        LastUsedAtUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Extrait le préfixe public d'une clé présentée.</summary>
    public static string? ExtractPrefix(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var parts = key.Split('_');

        // hba_<env>_<prefix>_<secret>
        return parts.Length == 4 && parts[0] == "hba" && parts[2].Length == PrefixLength
            ? parts[2]
            : null;
    }

    private static string ComputeHash(string value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
