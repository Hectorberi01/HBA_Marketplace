using System.Security.Cryptography;
using System.Text;
using HBA.Merchants.Application.Abstractions;

namespace HBA.Merchants.Infrastructure.Security;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE JETON D'INVITATION — TRENTE-DEUX OCTETS D'ALÉA, ET SON EMPREINTE SHA-256.
///
/// `RandomNumberGenerator` ET NON `Random`.
///
/// `Random` est un générateur pseudo-aléatoire prévisible : connaître quelques
/// jetons suffit à en deviner d'autres. Ici le jeton EST l'accès au dossier d'un
/// commerçant — c'est un secret, au même titre qu'un mot de passe.
///
/// PAS DE SEL, ET C'EST DÉLIBÉRÉ — CONTRAIREMENT À UN MOT DE PASSE.
///
/// Le sel et le coût de calcul protègent un secret À FAIBLE ENTROPIE, choisi par
/// un humain et donc devinable par dictionnaire. Ces trente-deux octets tirés au
/// hasard n'ont pas ce défaut : aucune table pré-calculée ne les couvre. Un SHA-256
/// nu suffit, et il permet la recherche par empreinte — ce qu'un hachage salé,
/// différent à chaque calcul, interdirait.
///
/// BASE64URL : LE JETON VOYAGE DANS UNE URL.
///
/// L'invité clique un lien. Un base64 ordinaire contient `+`, `/` et `=`, que les
/// clients de messagerie et les navigateurs ré-encodent chacun à leur façon — un
/// jeton qui ne correspond plus à son empreinte à l'arrivée, sans que rien
/// n'explique pourquoi.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class InvitationTokens : IInvitationTokens
{
    private const int OctetsDAlea = 32;

    public (string Token, string Hash) Create()
    {
        var octets = RandomNumberGenerator.GetBytes(OctetsDAlea);
        var token = Base64UrlTextEncoder(octets);

        return (token, Hash(token));
    }

    public string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64UrlTextEncoder(byte[] octets)
        => Convert.ToBase64String(octets)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
