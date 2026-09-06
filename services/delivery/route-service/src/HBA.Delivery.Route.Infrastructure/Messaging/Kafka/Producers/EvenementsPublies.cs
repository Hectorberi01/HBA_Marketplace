using HBA.Routes.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Kafka;

namespace HBA.Routes.Infrastructure.Messaging.Kafka.Producers;

/// <summary>
/// CE QUE CE SERVICE PUBLIE — LA MOITIE MANQUANTE DU MODULE.
///
/// `Consumers/` repond a « qu'est-ce que ce service ecoute ». Sans ce fichier,
/// « qu'est-ce qu'il emet » n'avait aucune reponse : il fallait chercher les
/// `PublishAsync` dans toute la couche Application, et on ne trouvait que ceux
/// qui existent — jamais celui qui manque.
///
/// LES PUBLICATIONS RESTENT DANS `Application`, ET IL NE FAUT PAS LES DEPLACER.
/// L'evenement doit etre mis en file LA OU LE FAIT METIER SE PRODUIT, pour que
/// `ModuleDbContext.SaveChangesAsync` le draine vers l'outbox DANS LA MEME
/// TRANSACTION. Ce dossier DECLARE, il ne publie pas.
///
/// CE QUE LA DECLARATION APPORTE. `HbaEventNaming.Describe` rend `null` quand un
/// evenement ne porte pas `[HbaEvent]`. La verification ci-dessous fait echouer
/// le DEMARRAGE plutot que de laisser decouvrir l'oubli a l'autre bout de la
/// plateforme, des semaines plus tard, dans une table qui reste vide.
///
/// CE QU'ELLE NE COUVRE PAS. Elle ne sait pas si un evenement publie quelque part
/// MANQUE a cette liste : rien ne relie un `PublishAsync` perdu dans Application
/// a ce fichier. La liste se tient a la main, et c'est sa faiblesse.
/// </summary>
public static class EvenementsPublies
{
    /// <summary>Les evenements publies par ce service, descripteur `[HbaEvent]` compris.</summary>
    public static readonly IReadOnlyList<Type> Types =
    [
        typeof(RouteCalculatedIntegrationEvent),
        typeof(RouteDeliveryEtaUpdatedIntegrationEvent),
        typeof(RouteRecalculatedIntegrationEvent),
    ];

    /// <summary>
    /// LES EVENEMENTS PUBLIES QUI N'ONT PAS ENCORE DE `[HbaEvent]`.
    ///
    /// Ils sont NOMMES ici plutot que passes sous silence. La verification les
    /// ignore volontairement : les faire echouer arreterait un service qui tourne
    /// aujourd'hui en production, pour un defaut qui n'a pas d'effet tant que
    /// `HbaEventNaming` n'est pas branche sur le fil (voir `HbaTopics`, §19.2).
    ///
    /// CE QU'ILS COUTENT DEJA. Leur nom d'evenement et leur sujet tombent sur le
    /// repli de `KafkaEventNaming`. Le jour ou le nommage canonique sera branche,
    /// ces evenements changeront de nom sur le fil — c'est cette liste qu'il
    /// faudra vider AVANT, pas apres.
    /// </summary>
    public static readonly IReadOnlyList<Type> SansDescripteur =
    [
        // aucun
    ];

    /// <summary>Refuse le demarrage si un evenement de `Types` n'a pas `[HbaEvent]`.</summary>
    internal static void VerifierLesDescripteurs()
    {
        var manquants = Types
            .Where(type => HbaEventNaming.Describe(type) is null)
            .Select(type => type.Name)
            .ToArray();

        if (manquants.Length > 0)
        {
            throw new InvalidOperationException(
                "Evenement(s) publie(s) sans descripteur [HbaEvent] : "
                + string.Join(", ", manquants)
                + ". Le nom et le sujet tomberaient sur un repli, et le consommateur "
                + "d'en face rejetterait le message en silence. Ajouter l'attribut, ou "
                + "inscrire l'evenement dans SansDescripteur en disant pourquoi.");
        }
    }
}
