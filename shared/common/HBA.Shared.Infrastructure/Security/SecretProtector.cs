using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using HBA.Shared.Application.Abstractions;

namespace HBA.Shared.Infrastructure.Security;

/// <summary>CHIFFREMENT DES SECRETS QUI TRAVERSENT LE BUS.</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string SectionName = "Security:SecretProtection";

    private const string Version = "v1";
    private const int TailleNonce = 12;
    private const int TailleEtiquette = 16;

    /// <summary>AES-256 : 32 octets, pas un de plus.</summary>
    private const int TailleCle = 32;

    private readonly byte[] _cle;

    public AesGcmSecretProtector(byte[] cle)
    {
        if (cle.Length != TailleCle)
        {
            throw new ArgumentException(
                $"La clé de protection des secrets doit faire {TailleCle} octets (AES-256), reçue : {cle.Length}.",
                nameof(cle));
        }

        _cle = cle;
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(TailleNonce);
        var clair = Encoding.UTF8.GetBytes(plaintext);
        var chiffre = new byte[clair.Length];
        var etiquette = new byte[TailleEtiquette];

        using var aes = new AesGcm(_cle, TailleEtiquette);
        aes.Encrypt(nonce, clair, chiffre, etiquette);

        return string.Join('.', Version, Base64Url(nonce), Base64Url(etiquette), Base64Url(chiffre));
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);

        var parties = protectedValue.Split('.');

        if (parties.Length != 4 || parties[0] != Version)
        {
            throw new CryptographicException(
                "Charge protégée illisible : préfixe de version absent ou inconnu. "
                + "Une valeur écrite avant la mise en place du chiffrement ne peut pas être déchiffrée.");
        }

        var nonce = DeBase64Url(parties[1]);
        var etiquette = DeBase64Url(parties[2]);
        var chiffre = DeBase64Url(parties[3]);
        var clair = new byte[chiffre.Length];

        using var aes = new AesGcm(_cle, TailleEtiquette);

        // LÈVE SI L'ÉTIQUETTE NE CORRESPOND PAS — altération, troncature, ou
        // simplement une autre clé.
        aes.Decrypt(nonce, chiffre, etiquette, clair);

        return Encoding.UTF8.GetString(clair);
    }

    /// <summary>Lit la clé depuis la configuration.</summary>
    public static AesGcmSecretProtector Depuis(IConfiguration configuration, bool estProduction)
    {
        var brut = configuration[$"{SectionName}:Key"];

        if (string.IsNullOrWhiteSpace(brut))
        {
            if (estProduction)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:Key est absente en production. Les codes de réinitialisation et de "
                    + "vérification traverseraient l'outbox et Kafka en clair. Générer 32 octets aléatoires "
                    + "en base64 et les fournir aux services identity et notifications — la MÊME clé des "
                    + "deux côtés, sans quoi les messages ne seront pas déchiffrables.");
            }

            Console.WriteLine(
                "[Secrets]  Aucune clé de protection configurée : clé de DÉVELOPPEMENT utilisée. "
                + "Les charges sont chiffrées, mais avec une clé publique — cela ne protège rien. "
                + $"Renseigner {SectionName}:Key hors développement.");

            return new AesGcmSecretProtector(CleDeDeveloppement());
        }

        byte[] cle;

        try
        {
            cle = Convert.FromBase64String(brut);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{SectionName}:Key n'est pas du base64 valide. Attendu : 32 octets encodés en base64.");
        }

        // LA TAILLE EST VERIFIEE ICI, ET LE MESSAGE NOMME LA CAUSE LA PLUS PROBABLE
        // — PARCE QUE LES RUNBOOKS L'ONT DICTEE.
        if (cle.Length != TailleCle)
        {
            var ressembleAHexadecimal =
                brut.Length == 64 && brut.All(Uri.IsHexDigit);

            var indice = ressembleAHexadecimal
                ? " La valeur fournie fait 64 caracteres hexadecimaux : elle vient tres "
                  + "probablement d'un `openssl rand -hex 32`, qui rend 48 octets une fois "
                  + "relu en base64. La commande juste est `openssl rand -base64 32`."
                : string.Empty;

            throw new InvalidOperationException(
                $"{SectionName}:Key fait {cle.Length} octets une fois decodee ; AES-256 en exige "
                + $"{TailleCle}." + indice);
        }

        return new AesGcmSecretProtector(cle);
    }

    /// <summary>Clé de développement, dérivée d'une phrase fixe.</summary>
    private static byte[] CleDeDeveloppement()
        => SHA256.HashData(Encoding.UTF8.GetBytes("hba-development-secret-protection-key"));

    private static string Base64Url(byte[] valeur)
        => Convert.ToBase64String(valeur).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeBase64Url(string valeur)
    {
        var normalise = valeur.Replace('-', '+').Replace('_', '/');

        // Le rembourrage a été retiré à l'encodage : on le remet pour que le
        // décodeur .NET accepte la chaîne.
        return Convert.FromBase64String(normalise.PadRight(normalise.Length + (4 - normalise.Length % 4) % 4, '='));
    }
}
