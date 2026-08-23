using HBA.Shared.Domain.Events;

namespace HBA.Media.Domain.Assets.Events;

/// <summary>
/// ─────────────────────────────────────────────────────────────────────────────
/// LES ÉVÉNEMENTS DU MÉDIA (cahier des charges §16).
///
/// Le cahier les nomme en sujets Kafka — <c>media.ready</c>, <c>media.deleted</c>.
/// Ils passent ici par l'outbox transactionnel : même garantie « au moins une
/// fois », sans second système à exploiter.
///
/// POURQUOI ILS COMPTENT MALGRÉ L'API SYNCHRONE.
///
/// Le §16 le dit : « les services métier peuvent écouter media.ready pour mettre
/// à jour leur état SANS COUPLAGE HTTP PERMANENT ». Un service qui devrait
/// interroger Media à chaque affichage pour savoir si la miniature existe ferait
/// de Media une dépendance de disponibilité — Media tombe, le catalogue tombe.
///
/// LE TYPE ET L'IDENTIFIANT DU PROPRIÉTAIRE VOYAGENT AVEC.
///
/// Sans eux, un consommateur recevrait « le média X est prêt » sans savoir s'il
/// le concerne, et devrait rappeler Media pour le découvrir. Ils voyagent en
/// CHAÎNE : un consommateur qui devrait référencer <c>MediaOwnerType</c> ferait
/// la dépendance que la frontière du module interdit.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed record MediaReadyDomainEvent(
    Guid MediaId, string OwnerType, Guid OwnerId, string MediaType, string ObjectKey) : DomainEvent;

/// <summary>
/// Le traitement a échoué.
///
/// CE N'EST PAS « le fichier est perdu ». L'original est intact et servable :
/// seules les variantes manquent. Un consommateur qui traiterait cet événement
/// comme une perte effacerait une photo parfaitement valable.
/// </summary>
public sealed record MediaProcessingFailedDomainEvent(
    Guid MediaId, string OwnerType, Guid OwnerId, string Reason) : DomainEvent;

/// <summary>
/// Supprimé LOGIQUEMENT.
///
/// C'est le signal qui permet au service propriétaire d'oublier le
/// <c>MediaId</c> — une fiche produit qui garderait la référence d'un média
/// supprimé afficherait une image morte jusqu'à ce que quelqu'un s'en plaigne.
/// </summary>
public sealed record MediaDeletedDomainEvent(
    Guid MediaId, string OwnerType, Guid OwnerId, string MediaType) : DomainEvent;
