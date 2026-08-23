using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using HBA.Shared.Application.Abstractions;

namespace HBA.Shared.Infrastructure.Security;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CHIFFREMENT DES SECRETS QUI TRAVERSENT LE BUS.
///
/// ÉCRIT PARCE QUE LES CODES DE RÉINITIALISATION CIRCULAIENT EN CLAIR.
///
/// `PasswordResetRequestedIntegrationEvent` et
/// `EmailVerificationRequestedIntegrationEvent` transportaient le code tel quel. Il
/// partait donc sur un topic Kafka (rétention 7 jours en production) ET il était
/// écrit en clair dans `identity.outbox_messages.Content`, table que rien ne
/// purgeait. Un accès en LECTURE — une sauvegarde, un export analytique, un compte
/// de consultation — suffisait à prendre n'importe quel compte : le code EST le
/// justificatif, la boîte mail n'est que le canal de livraison.
///
/// CE QUE CE CHIFFREMENT PROTÈGE, ET CONTRE QUI.
///
/// Il protège contre quiconque lit **la donnée** sans avoir **la clé** : dump de
/// base, sauvegarde, réplica analytique, consommateur du topic, journal qui aurait
/// recopié une charge. C'est exactement la surface qui posait problème.
///
/// Il ne protège PAS contre quelqu'un qui a la clé — donc contre une compromission
/// des services identity ou notifications eux-mêmes. Ce n'est pas l'objectif : un
/// service compromis a de toute façon accès au secret puisqu'il doit l'envoyer.
///
/// AES-GCM, DONC CHIFFREMENT **AUTHENTIFIÉ**.
///
/// Pas AES-CBC : sans authentification, un attaquant capable d'écrire dans le topic
/// pourrait modifier le chiffré. Le déchiffrement rendrait alors des octets
/// arbitraires sans que rien ne le signale. GCM porte une étiquette
/// d'authentification : une charge altérée LÈVE au déchiffrement.
///
/// NONCE ALÉATOIRE DE 12 OCTETS, UN PAR MESSAGE.
///
/// Réutiliser un nonce avec la même clé casse GCM complètement — ce n'est pas une
/// faiblesse théorique, c'est une perte totale de confidentialité et
/// d'authenticité. Il est donc tiré au hasard à chaque appel, jamais dérivé du
/// contenu, jamais compté.
///
/// FORMAT VERSIONNÉ : `v1.&lt;nonce&gt;.&lt;étiquette&gt;.&lt;chiffré&gt;`, chaque partie en
/// base64url. Le préfixe existe pour qu'une rotation de clé ou un changement
/// d'algorithme soit possible sans deviner ce qu'on lit. Une charge sans préfixe
/// connu est refusée plutôt que devinée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string SectionName = "Security:SecretProtection";

    private const string Version = "v1";
    private const int TailleNonce = 12;
    private const int TailleEtiquette = 16;

    private readonly byte[] _cle;

    public AesGcmSecretProtector(byte[] cle)
    {
        if (cle.Length != 32)
        {
            throw new ArgumentException(
                $"La clé de protection des secrets doit faire 32 octets (AES-256), reçue : {cle.Length}.",
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
        // simplement une autre clé. On laisse remonter : un secret qu'on ne sait
        // pas déchiffrer ne doit surtout pas être remplacé par une valeur par
        // défaut. Le message part en lettre morte, ce qui est visible.
        aes.Decrypt(nonce, chiffre, etiquette, clair);

        return Encoding.UTF8.GetString(clair);
    }

    /// <summary>
    /// Lit la clé depuis la configuration.
    ///
    /// REFUSE DE DÉMARRER EN PRODUCTION SANS CLÉ. C'est la règle du dépôt, la
    /// même que pour les passerelles de paiement, l'e-mail et le stockage objet :
    /// un adaptateur silencieux fait « tourner » la plateforme dans un état faux.
    /// Ici, l'état faux serait de renvoyer les codes en clair sur le bus — donc de
    /// réintroduire le défaut qu'on vient de fermer, sans que personne ne le voie.
    ///
    /// Hors production, une clé de développement fixe est utilisée et ANNONCÉE. Elle
    /// est publique, comme les autres secrets de `docker-compose.dev.yml` — ce qui
    /// est assumé et écrit en tête de ce fichier-là.
    /// </summary>
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

        return new AesGcmSecretProtector(cle);
    }

    /// <summary>
    /// Clé de développement, dérivée d'une phrase fixe. Volontairement reproductible :
    /// deux services lancés séparément doivent pouvoir se lire sans coordination.
    /// </summary>
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
