using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Kafka;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Producers;

/// <summary>CE QUE CE SERVICE PUBLIE — LA MOITIE MANQUANTE DU MODULE.</summary>
public static class EvenementsPublies
{
    /// <summary>Les evenements publies par ce service, descripteur `[HbaEvent]` compris.</summary>
    public static readonly IReadOnlyList<Type> Types =
    [
        typeof(ReturnRefundApprovedIntegrationEvent),
        typeof(ReturnRefundedIntegrationEvent),
    ];

    /// <summary>PLUS AUCUN EVENEMENT PUBLIE NE MANQUE DE `[HbaEvent]`.</summary>
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
                + "d'en face rejetterait le message en silence. Ajouter l'attribut, ou "
                + "inscrire l'evenement dans SansDescripteur en disant pourquoi.");
        }
    }
}
