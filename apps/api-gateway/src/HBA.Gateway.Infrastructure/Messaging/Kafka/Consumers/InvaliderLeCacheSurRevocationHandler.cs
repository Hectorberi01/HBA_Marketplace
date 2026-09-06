using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// FERME LA FENETRE DE TRENTE SECONDES PENDANT LAQUELLE UN JETON REVOQUE PASSAIT.
///
/// CE QUI SE PASSAIT. `TokenRevocationMiddleware` demande le verdict a
/// identity-service, puis le met en cache `TokenRevocationOptions.CacheSeconds`
/// — trente secondes par defaut. Une deconnexion, un changement de mot de passe,
/// une suspension administrative ou une revocation par un administrateur ne
/// mordaient donc qu'au bout de ce delai. Sur `ADMIN_REVOKE` et `SUSPENDED`,
/// c'est-a-dire exactement les cas ou l'on veut couper l'acces TOUT DE SUITE,
/// l'acces restait ouvert une demi-minute.
///
/// `TokenRevokedIntegrationEvent` existait depuis le §10.1, portait le `UserId`
/// et la raison, et N'ETAIT CONSOMME PAR PERSONNE.
///
/// CE QUE CE GESTIONNAIRE CHANGE. Il evince les verdicts du compte des reception
/// du message. La fenetre tombe de trente secondes a la latence du bus.
///
/// C'EST LE PREMIER CONSOMMATEUR KAFKA DE LA PASSERELLE, ET CE N'EST PAS ANODIN.
///
/// Le composant en frontal depend desormais du bus pour une propriete de
/// SECURITE. Si Kafka est indisponible, ce gestionnaire ne s'execute pas et l'on
/// retombe exactement sur le comportement d'avant — trente secondes de
/// tolerance, pas davantage. La degradation est donc bornee, et c'est ce qui
/// rend le compromis acceptable : le bus AMELIORE le delai, il n'est pas ce qui
/// fait tenir le controle.
///
/// PAS D'INBOX, ET C'EST CORRECT. Evincer une entree de cache deux fois n'a
/// aucun effet supplementaire. Le repartiteur journalisera « aucun
/// IConsumerInbox enregistre » au premier message : c'est attendu ici, la
/// passerelle n'a pas de base.
///
/// CE QUE ÇA NE COUVRE PAS. Le jeton reste cryptographiquement valide jusqu'a
/// son expiration naturelle : on evince un CACHE, on ne revoque pas un JWT. Un
/// appel qui contourne la passerelle n'est pas concerne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[NomDeConsommateur("HBA.Gateway.Infrastructure.Messaging.Kafka.Consumers.InvaliderLeCacheSurRevocationHandler")]
public sealed class InvaliderLeCacheSurRevocationHandler : IIntegrationEventHandler<TokenRevokedIntegrationEvent>
{
    private readonly RegistreDeRevocation _registre;
    private readonly ILogger<InvaliderLeCacheSurRevocationHandler> _logger;

    public InvaliderLeCacheSurRevocationHandler(
        RegistreDeRevocation registre,
        ILogger<InvaliderLeCacheSurRevocationHandler> logger)
    {
        _registre = registre;
        _logger = logger;
    }

    public Task HandleAsync(TokenRevokedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var evince = _registre.Revoquer(e.UserId);

        // `LogDebug` QUAND RIEN N'EST EVINCE, ET C'EST VOLONTAIRE.
        //
        // Chaque replique recoit le message, une seule avait peut-etre servi ce
        // compte. « Rien a evincer » est donc le cas MAJORITAIRE et normal — en
        // faire un avertissement remplirait les journaux d'un bruit qui ferait
        // ignorer les vrais.
        if (evince)
        {
            _logger.LogInformation(
                "Verdicts de révocation évincés pour le compte {UserId} — raison : {Reason}.",
                e.UserId, e.Reason);
        }
        else
        {
            _logger.LogDebug(
                "Révocation reçue pour le compte {UserId} : aucune entrée en cache sur cette instance.",
                e.UserId);
        }

        return Task.CompletedTask;
    }
}
