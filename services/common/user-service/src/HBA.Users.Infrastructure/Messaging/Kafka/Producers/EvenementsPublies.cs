using HBA.Shared.Infrastructure.Kafka;
using HBA.Users.Contracts.IntegrationEvents;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Producers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE user-service PUBLIE — LA MOITIÉ MANQUANTE DU MODULE.
///
/// `Consumers/` répond à « qu'est-ce que ce service écoute ». Sans ce fichier,
/// « qu'est-ce qu'il émet » n'avait aucune réponse : il fallait chercher les
/// `PublishAsync` dans toute la couche Application, et on ne trouvait que ceux
/// qui existent — jamais celui qui manque.
///
/// LES PUBLICATIONS RESTENT DANS `Application`, ET IL NE FAUT PAS LES DÉPLACER.
///
/// C'est le point important de ce fichier. `AddAddressCommand`,
/// `ProfileCommands` et `DeviceUseCases` appellent `IIntegrationEventPublisher`
/// eux-mêmes, et c'est correct : l'événement doit être mis en file LÀ OÙ LE FAIT
/// MÉTIER SE PRODUIT, pour que `ModuleDbContext.SaveChangesAsync` le draine vers
/// l'outbox DANS LA MÊME TRANSACTION que le changement d'état.
///
/// Les faire passer par une classe d'infrastructure les sortirait de cette
/// transaction : l'adresse serait enregistrée et l'événement perdu sur une panne
/// entre les deux. C'est exactement ce que l'outbox existe pour empêcher.
///
/// Ce dossier DÉCLARE donc, il ne publie pas. Un `Producers/` qui contiendrait
/// des méthodes d'envoi serait une régression déguisée en rangement.
///
/// CE QUE LA DÉCLARATION APPORTE, ET CE N'EST PAS DE LA DOCUMENTATION.
///
/// `HbaEventNaming.Describe` rend `null` quand un événement ne porte pas
/// `[HbaEvent]` — son propre commentaire dit « c'est-à-dire s'il n'a pas encore
/// été migré ». Le nom de l'événement et son sujet retombent alors sur un repli,
/// et le consommateur d'en face cherche un type qu'il ne trouvera pas :
/// « événement reçu et NON RECONNU ». Rien ne lève, rien n'échoue, l'effet
/// métier n'a simplement pas lieu.
///
/// La vérification ci-dessous fait échouer l'ENREGISTREMENT du service — donc le
/// démarrage — plutôt que de laisser découvrir l'oubli à l'autre bout de la
/// plateforme, des semaines plus tard, dans une table qui reste vide.
///
/// CE QU'ELLE NE COUVRE PAS. Elle ne sait pas si un événement publié quelque
/// part MANQUE à cette liste : rien ne relie un `PublishAsync` perdu dans
/// Application à ce fichier. Fermer ce trou demanderait une analyse du code à la
/// compilation. En attendant, la liste se tient à la main, et c'est sa faiblesse.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class EvenementsPublies
{
    /// <summary>
    /// Les trois événements de ce service, et d'où ils partent.
    ///
    /// Tous trois déclarent le domaine `user` : ils atterrissent donc sur
    /// `service.user.v1`, le sujet que les autres services écoutent pour suivre
    /// les profils.
    /// </summary>
    public static readonly IReadOnlyList<Type> Types =
    [
        typeof(UserProfileChangedIntegrationEvent),
        typeof(UserAddressCreatedIntegrationEvent),
        typeof(UserDeviceRegisteredIntegrationEvent),
    ];

    /// <summary>
    /// Refuse le démarrage si un événement déclaré ne porte pas `[HbaEvent]`.
    /// </summary>
    /// <remarks>
    /// LE MESSAGE NOMME LE TYPE, PAS LE SYMPTÔME. « NON RECONNU » côté
    /// consommateur arrive trop tard et au mauvais endroit : c'est le service qui
    /// PUBLIE qui a l'information, et c'est ici qu'elle est utile.
    /// </remarks>
    internal static void VerifierLesDescripteurs()
    {
        var sansDescripteur = Types
            .Where(type => HbaEventNaming.Describe(type) is null)
            .Select(type => type.Name)
            .ToArray();

        if (sansDescripteur.Length > 0)
        {
            throw new InvalidOperationException(
                $"{sansDescripteur.Length} événement(s) publié(s) par user-service ne portent pas "
                + $"l'attribut [HbaEvent] : {string.Join(", ", sansDescripteur)}. Sans lui, le nom "
                + "et le sujet retombent sur un repli, et le consommateur d'en face journalise "
                + "« événement reçu et NON RECONNU » — sans erreur, sans échec, et sans effet "
                + "métier. Déclarer domaine, agrégat, action et version sur le contrat.");
        }
    }
}
