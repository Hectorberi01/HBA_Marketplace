using HBA.Communication.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Kafka;

namespace HBA.Communication.Infrastructure.Messaging.Kafka.Producers;

/// <summary>
/// CE QUE LA MESSAGERIE INTERNE PUBLIE — UN SEUL EVENEMENT.
///
/// `MessageSent` est consomme par le module Notifications, compose dans le meme
/// hote. Il passe malgre tout par Kafka et non par un appel direct : les deux
/// modules ont chacun leur base, et un appel en memoire lierait leur
/// transaction — un message enregistre sans notification, ou l'inverse.
///
/// LES PUBLICATIONS RESTENT DANS `Application`. Ce dossier DECLARE, il ne publie
/// pas : voir `user-service` pour la raison.
/// </summary>
public static class EvenementsPublies
{
    public static readonly IReadOnlyList<Type> Types =
    [
        typeof(MessageSentIntegrationEvent),
    ];

    /// <summary>
    /// PLUS AUCUN EVENEMENT PUBLIE NE MANQUE DE `[HbaEvent]`.
    ///
    /// Y inscrire un evenement serait une derogation : son nom sur le fil
    /// tomberait sur un repli le jour ou le nommage canonique sera branche.
    /// </summary>
    public static readonly IReadOnlyList<Type> SansDescripteur = [];

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
                + "d'en face rejetterait le message en silence.");
        }
    }
}
