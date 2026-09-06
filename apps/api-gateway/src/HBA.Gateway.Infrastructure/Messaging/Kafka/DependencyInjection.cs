using HBA.Gateway.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Gateway.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PASSERELLE CONSOMME DU KAFKA — UN SEUL EVENEMENT, POUR UNE PROPRIETE DE
///     SECURITE.
///
/// Elle n'en consommait aucun. Le seul motif de l'y mettre est `TokenRevoked` :
/// voir `InvaliderLeCacheSurRevocationHandler` pour la fenetre de trente
/// secondes qu'il ferme.
///
/// `AddBuildingBlocksInfrastructure` FAIT PLUS QUE CE QU'ON DEMANDE, ET ON
///     L'APPELLE QUAND MEME.
///
/// Il enregistre le consommateur Kafka, mais aussi le publieur, la file
/// d'evenements, le repartiteur, le cache distribue et les compteurs a vide.
/// Trier a la main pour ne garder que le consommateur donnerait une SECONDE
/// façon d'assembler le socle, qui divergerait au premier changement amont — la
/// classe de panne qui a coute cette semaine entiere. On prend le bloc, et on
/// paie ce qu'il pese.
///
/// CE QU'IL FAUT POSER DANS L'ENVIRONNEMENT. `OUTBOX_ENABLED=false` : la
/// passerelle n'a pas de base, donc pas de table d'outbox, et le socle REFUSE de
/// demarrer si le drainage est actif sans producteur joignable. C'est pose dans
/// les deux composes.
///
/// LE GROUPE DE CONSOMMATION EST PAR INSTANCE, ET IL EST FORGE ICI.
///
/// Dans un groupe partage, UNE SEULE replique recoit chaque message. Le cache de
/// revocation vivant en memoire de processus, les autres repliques
/// continueraient de servir un verdict perime — la correction ne marcherait que
/// sur une instance au hasard, sans que rien ne le dise.
///
/// LE COMPOSE NE PEUT PAS LE FAIRE. `${HOSTNAME}` y est substitue par le SHELL
/// qui lance `docker compose`, pas par le conteneur : les trois repliques
/// recevraient la meme valeur — le nom de l'hote — et se retrouveraient dans le
/// MEME groupe. Le piege est d'autant plus mauvais qu'il a l'air correct.
///
/// D'ou la surcharge ci-dessous, posee dans la configuration juste avant que le
/// socle la lise. `Environment.MachineName` vaut, dans un conteneur, son
/// identifiant court : un groupe par replique, et chacune recoit TOUT.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterMessagerieGateway(
        this IServiceCollection services, IConfigurationManager configuration)
    {
        // POSEE AVANT `AddBuildingBlocksInfrastructure`, QUI LIT LA VALEUR.
        //
        // Ajoutee en DERNIER dans la chaîne de configuration, donc prioritaire sur
        // la variable d'environnement. Voir l'encadre de cette classe pour la
        // raison — un groupe partage rendrait la correction inoperante sur toutes
        // les repliques sauf une.
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:ConsumerGroup"] = $"hba-gateway-{Environment.MachineName}"
        });

        services.AddBuildingBlocksInfrastructure(configuration);
        services.AjouterSujetsGateway();

        // Singleton : le registre porte l'etat qui relie le middleware — lui aussi
        // unique pour le processus — au gestionnaire, resolu par message.
        services.AddSingleton<RegistreDeRevocation>();

        services.AddScoped<
            IIntegrationEventHandler<TokenRevokedIntegrationEvent>,
            InvaliderLeCacheSurRevocationHandler>();

        return services;
    }
}
