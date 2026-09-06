using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure.Kafka;

namespace HBA.Merchants.Infrastructure.Messaging.Kafka.Producers;

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
        typeof(KybDocumentRemovedIntegrationEvent),
        typeof(SellerActivatedIntegrationEvent),
        typeof(SellerClosedIntegrationEvent),
        typeof(SellerDeletedIntegrationEvent),
        typeof(SellerKybApprovedIntegrationEvent),
        typeof(SellerKybRejectedIntegrationEvent),
        typeof(SellerKybSubmittedIntegrationEvent),
        typeof(SellerMemberActivatedIntegrationEvent),
        typeof(SellerMemberInvitedIntegrationEvent),
        typeof(SellerMemberJoinedIntegrationEvent),
        typeof(SellerMemberRevokedIntegrationEvent),
        typeof(SellerMemberRolesUpdatedIntegrationEvent),
        typeof(SellerMemberStoreAssignedIntegrationEvent),
        typeof(SellerMemberStoreUnassignedIntegrationEvent),
        typeof(SellerMemberSuspendedIntegrationEvent),
        typeof(SellerOwnershipTransferredIntegrationEvent),
        typeof(SellerReactivatedIntegrationEvent),
        typeof(SellerRegisteredIntegrationEvent),
        typeof(SellerSuspendedIntegrationEvent),
        typeof(SellerSuspensionLiftedIntegrationEvent),
        typeof(StoreClosedIntegrationEvent),
        typeof(StoreOpenedIntegrationEvent),
        typeof(StoreSuspendedIntegrationEvent),
        typeof(StoreSuspensionLiftedIntegrationEvent),
    ];

    /// <summary>
    /// PLUS AUCUN EVENEMENT PUBLIE NE MANQUE DE `[HbaEvent]`.
    ///
    /// Cette liste nommait les evenements que la verification devait ignorer,
    /// parce que les faire echouer aurait arrete des services qui tournaient. Les
    /// quatre-vingts concernes ont recu leur descripteur : elle est vide, et elle
    /// doit le rester.
    ///
    /// CE QU'ELLE REDEVIENT SI ELLE SE REMPLIT. Une derogation. Y inscrire un
    /// evenement, c'est dire que son nom sur le fil tombera sur un repli le jour
    /// ou le nommage canonique sera branche — donc qu'un consommateur cherchera un
    /// nom qui n'existe pas. La raison doit etre ecrite a cote.
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
                + "d'en face rejetterait le message en silence. Ajouter l'attribut, ou "
                + "inscrire l'evenement dans SansDescripteur en disant pourquoi.");
        }
    }
}
