using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI PERMET D'EVINCER LES VERDICTS D'UN COMPTE SANS CONNAITRE SES JETONS.
///
/// LE PROBLEME. `TokenRevocationMiddleware` met en cache un verdict PAR JETON —
/// la cle est l'empreinte du jeton. La revocation, elle, arrive PAR COMPTE :
/// `TokenRevokedIntegrationEvent` porte un `UserId`. Or `IMemoryCache` ne
/// s'enumere pas : impossible de retrouver « toutes les entrees de ce compte ».
///
/// LA SOLUTION. Chaque entree est posee avec un jeton d'expiration lie au
/// compte. Annuler la source du compte evince d'un coup toutes ses entrees,
/// quels que soient leur nombre et leur nom.
///
/// CE QU'IL FAUT SAVOIR SUR LA MEMOIRE. Une source par compte VU, creee a la
/// premiere requete authentifiee et retiree a la revocation. Le dictionnaire est
/// donc borne par le nombre de comptes actifs depuis le demarrage du processus,
/// pas par le nombre de jetons. Il n'est pas purge autrement : un redemarrage le
/// vide, et c'est acceptable pour un objet de quelques dizaines d'octets.
///
/// CE QUE CE REGISTRE NE FAIT PAS. Il ne franchit pas la frontiere du processus.
/// Chaque replique de la passerelle a le sien, et doit donc RECEVOIR le message
/// Kafka — d'ou le groupe de consommation PAR INSTANCE impose dans le compose.
/// Avec un groupe partage, une seule replique evincerait, et les autres
/// serviraient le verdict perime jusqu'au bout de leurs trente secondes, sans
/// que rien ne le signale.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RegistreDeRevocation : IDisposable
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _sources = new();

    /// <summary>Le jeton d'expiration a attacher aux entrees de cache de ce compte.</summary>
    public IChangeToken JetonDExpiration(Guid utilisateur)
        => new CancellationChangeToken(_sources.GetOrAdd(utilisateur, _ => new CancellationTokenSource()).Token);

    /// <summary>
    /// Evince toutes les entrees de cache de ce compte. Sans effet si le compte
    /// n'a jamais ete vu par CETTE instance — ce qui est le cas normal, pas une
    /// anomalie : la revocation est diffusee a toutes les repliques, et une seule
    /// avait peut-etre servi ce compte.
    /// </summary>
    public bool Revoquer(Guid utilisateur)
    {
        if (!_sources.TryRemove(utilisateur, out var source))
        {
            return false;
        }

        source.Cancel();
        source.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (var source in _sources.Values)
        {
            source.Dispose();
        }

        _sources.Clear();
    }
}
