using HBA.Shared.Infrastructure.Kafka;
using HBA.Users.Contracts.IntegrationEvents;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Producers;

/// <summary>CE QUE user-service PUBLIE — LA MOITIÉ MANQUANTE DU MODULE.</summary>
public static class EvenementsPublies
{
    /// <summary>Les trois événements de ce service, et d'où ils partent.</summary>
    public static readonly IReadOnlyList<Type> Types =
    [
        typeof(UserProfileChangedIntegrationEvent),
        typeof(UserAddressCreatedIntegrationEvent),
        typeof(UserDeviceRegisteredIntegrationEvent),
    ];

    /// <summary>Refuse le démarrage si un événement déclaré ne porte pas `[HbaEvent]`.</summary>
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
