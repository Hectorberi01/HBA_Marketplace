using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>Attestation d'identité de l'appelant sur un appel gRPC interne.</summary>
public static class IdentiteInterne
{
    /// <summary>Métadonnée gRPC portant l'attestation.</summary>
    public const string MetadataKey = "x-internal-identity";

    /// <summary>Durée de validité d'une attestation, à la frappe.</summary>
    public static readonly TimeSpan Duree = TimeSpan.FromSeconds(30);

    /// <summary>Tolérance d'horloge acceptée à la vérification.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

    private const string Version = "1";
    private const char Separateur = '|';

    // Le décodage d'une clé DER coûte quelques dizaines de microsecondes : refait à
    // chaque appel, il pèserait plus que la signature elle-même.
    private static readonly ConcurrentDictionary<string, ECDsa> _clesPubliques = new();
    private static readonly ConcurrentDictionary<string, ECDsa> _clesPrivees = new();

    /// <summary>Fabrique l'attestation qu'un appelant joint à son appel.</summary>
    /// <param name="appelant">Nom de l'hôte appelant, ex.</param>
    /// <param name="methode">Méthode gRPC complète, ex.</param>
    /// <param name="clePriveeBase64">PKCS#8 en base64 — la clé privée de CET hôte.</param>
    /// <param name="maintenant">
    /// Injecté pour les tests ; sinon <see cref="DateTimeOffset.UtcNow"/> .
    /// </param>
    public static string Signer(
        string appelant, string methode, string clePriveeBase64, DateTimeOffset? maintenant = null)
    {
        // LE SÉPARATEUR NE DOIT PAS APPARAÎTRE DANS LES CHAMPS.
        if (appelant.Contains(Separateur) || methode.Contains(Separateur))
        {
            throw new InvalidOperationException(
                "Un nom d'appelant ou de méthode ne peut pas contenir '|'.");
        }

        var expiration = (maintenant ?? DateTimeOffset.UtcNow).Add(Duree).ToUnixTimeSeconds();

        // `jti` n'est vérifié par personne aujourd'hui : il n'existe ni cache
        // anti-rejeu ni journal d'audit des appels internes.
        var jti = Convertir(RandomNumberGenerator.GetBytes(9));

        var charge = $"{Version}{Separateur}{appelant}{Separateur}{methode}{Separateur}{expiration}{Separateur}{jti}";
        var octets = Encoding.UTF8.GetBytes(charge);

        var cle = _clesPrivees.GetOrAdd(clePriveeBase64, ImporterClePrivee);

        var signature = cle.SignData(octets, HashAlgorithmName.SHA256);

        return $"{Convertir(octets)}.{Convertir(signature)}";
    }

    /// <summary>Vérifie une attestation et rend le nom de l'appelant, ou `null`.</summary>
    /// <param name="attestation">Contenu de la métadonnée <see cref="MetadataKey"/>.</param>
    /// <param name="methodeAttendue">La méthode réellement invoquée.</param>
    /// <param name="registre">Clés publiques connues, par nom d'hôte.</param>
    public static string? Verifier(
        string? attestation,
        string methodeAttendue,
        IReadOnlyDictionary<string, string> registre,
        DateTimeOffset? maintenant = null)
    {
        if (string.IsNullOrWhiteSpace(attestation))
        {
            return null;
        }

        var point = attestation.IndexOf('.');
        if (point <= 0 || point == attestation.Length - 1)
        {
            return null;
        }

        byte[] octets;
        byte[] signature;
        try
        {
            octets = Reconvertir(attestation[..point]);
            signature = Reconvertir(attestation[(point + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }

        var champs = Encoding.UTF8.GetString(octets).Split(Separateur);
        if (champs.Length != 5 || champs[0] != Version)
        {
            return null;
        }

        var appelant = champs[1];
        var methode = champs[2];

        // LA MÉTHODE EST VÉRIFIÉE AVANT LA SIGNATURE, ET C'EST SANS DANGER.
        if (!string.Equals(methode, methodeAttendue, StringComparison.Ordinal))
        {
            return null;
        }

        if (!long.TryParse(champs[3], out var expiration))
        {
            return null;
        }

        var instant = maintenant ?? DateTimeOffset.UtcNow;

        if (DateTimeOffset.FromUnixTimeSeconds(expiration) < instant - Tolerance)
        {
            return null;
        }

        // UNE EXPIRATION TROP LOINTAINE EST REFUSÉE, MÊME SIGNÉE.
        if (DateTimeOffset.FromUnixTimeSeconds(expiration) > instant + Duree + Tolerance)
        {
            return null;
        }

        if (!registre.TryGetValue(appelant, out var clePublique)
            || string.IsNullOrWhiteSpace(clePublique))
        {
            return null;
        }

        ECDsa cle;
        try
        {
            cle = _clesPubliques.GetOrAdd(clePublique, valeur =>
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(valeur), out _);
                return ecdsa;
            });
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            // Clé publique illisible dans le registre = faute de configuration, pas
            // faute de l'appelant.
            return null;
        }

        return cle.VerifyData(octets, signature, HashAlgorithmName.SHA256) ? appelant : null;
    }

    /// <summary>Lit le registre `nom=base64;nom=base64` des clés publiques.</summary>
    public static IReadOnlyDictionary<string, string> LireRegistre(string? valeur)
    {
        var registre = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(valeur))
        {
            return registre;
        }

        foreach (var entree in valeur.Split(';', StringSplitOptions.RemoveEmptyEntries
                                                 | StringSplitOptions.TrimEntries))
        {
            var egal = entree.IndexOf('=');
            if (egal <= 0 || egal == entree.Length - 1)
            {
                continue;
            }

            registre[entree[..egal].Trim()] = entree[(egal + 1)..].Trim();
        }

        return registre;
    }

    /// <summary>Refuse le mode non signé partout sauf en développement.</summary>
    public static void RefuserLeModeNonSigneHorsDeveloppement(
        bool identitesNonSignees, bool estDeveloppement)
    {
        if (identitesNonSignees && !estDeveloppement)
        {
            throw new InvalidOperationException(
                "Internal:IdentitesNonSignees n'est autorisé qu'en environnement "
                + "Development. Fournir Internal:PrivateKey et Internal:PublicKeys.");
        }
    }

    /// <summary>Refuse une clé privée que <see cref="Signer"/> ne saura pas lire.</summary>
    public static void RefuserUneClePriveeIllisible(string? clePriveeBase64)
    {
        // Vide est un état légitime : l'hôte n'émet alors aucun appel signé, et
        // `InternalCallClientInterceptor` lève un `FailedPrecondition` explicite au
        // moment où il en aurait eu besoin.
        if (string.IsNullOrWhiteSpace(clePriveeBase64))
        {
            return;
        }

        ImporterClePrivee(clePriveeBase64).Dispose();
    }

    /// <summary>Décode une clé privée, quel que soit son encodage DER.</summary>
    private static ECDsa ImporterClePrivee(string clePriveeBase64)
    {
        byte[] octets;

        try
        {
            octets = Convert.FromBase64String(clePriveeBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{InternalCallOptions.SectionName}:PrivateKey n'est pas du base64 valide. "
                + "Attendu : une clé EC P-256, en DER, encodée en base64 sur une seule ligne. "
                + "La produire avec scripts/generer-identites-internes.sh.");
        }

        var ecdsa = ECDsa.Create();

        try
        {
            ecdsa.ImportPkcs8PrivateKey(octets, out _);
            return ecdsa;
        }
        catch (CryptographicException)
        {
            // Pas du PKCS#8. Reste la forme traditionnelle, que produit `openssl
            // genpkey -outform DER` — donc la plus probable des deux.
        }

        try
        {
            ecdsa.ImportECPrivateKey(octets, out _);
            return ecdsa;
        }
        catch (CryptographicException exception)
        {
            ecdsa.Dispose();

            throw new InvalidOperationException(
                $"{InternalCallOptions.SectionName}:PrivateKey ne se lit ni comme une clé PKCS#8 "
                + $"ni comme une clé EC traditionnelle ({octets.Length} octets une fois décodée). "
                + "Les DEUX encodages DER d'une clé EC P-256 sont acceptés ; ce n'est aucun des "
                + "deux. Une valeur produite par `openssl rand -base64 32` en fait 32 et donne "
                + "cette erreur : c'est de l'aléa, pas une clé. Engendrer la bonne valeur avec "
                + "scripts/generer-identites-internes.sh.",
                exception);
        }
    }


    // BASE64URL À LA MAIN : `Base64Url` EST .NET 9, CE PROJET EST net8.0.
    private static string Convertir(byte[] octets)
        => Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Reconvertir(string valeur)
    {
        var brut = valeur.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(brut.PadRight((brut.Length + 3) / 4 * 4, '='));
    }
}
