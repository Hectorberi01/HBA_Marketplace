using HBA.Shared.Domain.Events;

namespace HBA.Deliveries.Domain.Deliveries.Events;

/// <summary>
/// Faits métier émis par l'agrégat <see cref="Delivery"/>.
///
/// Ils restent INTERNES au module : leur traduction en événements d'intégration
/// — ceux que consomment Order, Wallet ou un webhook partenaire — se fait en
/// Infrastructure. Le domaine ne connaît ni le bus, ni les abonnés.
/// </summary>
public sealed record DeliveryCreatedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    DeliveryType Type) : DomainEvent;

/// <summary>Le dispatch a commencé à chercher un livreur.</summary>
public sealed record DeliverySearchingDriverDomainEvent(Guid DeliveryId, int AttemptNumber) : DomainEvent;

/// <summary>Une mission a été proposée à un livreur.</summary>
public sealed record DeliveryAssignedDomainEvent(Guid DeliveryId, Guid DriverId) : DomainEvent;

/// <summary>
/// Le livreur a accepté.
///
/// PORTE LA RÉFÉRENCE ET LA SOURCE, comme tous les événements qui franchiront
/// la frontière du module. L'agrégat les connaît déjà : les omettre obligerait le
/// traducteur en événement d'intégration à relire la course en base — une lecture
/// de plus par événement, et surtout un mode de défaillance nouveau, sur un
/// chemin où l'échec signifie « le client ne sera jamais prévenu ».
/// </summary>
public sealed record DeliveryAcceptedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId) : DomainEvent;

/// <summary>
/// Le livreur a refusé. Ce n'est PAS un incident : c'est le fonctionnement normal
/// du dispatch, et l'événement sert à relancer la recherche, pas à alerter.
/// </summary>
public sealed record DeliveryRejectedByDriverDomainEvent(Guid DeliveryId, Guid DriverId, string? Reason) : DomainEvent;

/// <summary>Le colis est pris en charge.</summary>
/// <param name="IssuedPin">
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CODE DE REMISE, PARCE QUE PERSONNE NE LE PORTAIT AU DESTINATAIRE.
///
/// LA PREUVE PAR PIN ÉTAIT CORRECTE ET INUTILISABLE.
///
/// `Delivery.Create` tire un code aléatoire et le persiste, `ProofOfDelivery`
/// sait le vérifier — et **aucun canal ne le donnait à l'acheteur**. Un code que
/// le destinataire ne connaît pas ne prouve rien : la remise se terminait par la
/// voie média, c'est-à-dire sans le contrôle qu'on croyait avoir posé.
///
/// POURQUOI À L'ENLÈVEMENT, ET NON À LA CRÉATION.
///
/// C'est le moment où le code devient ACTIONNABLE : le colis est en route, la
/// remise est proche. L'envoyer à la création — parfois des heures plus tôt —
/// l'aurait noyé dans les notifications de la commande, et le client l'aurait
/// cherché au mauvais endroit au moment de la remise.
///
/// Le prix de ce choix : si la notification échoue à cet instant, il n'y a pas de
/// seconde chance automatique. Une route « renvoyer mon code » reste à écrire.
///
/// NUL QUAND LA PREUVE N'EST PAS UN PIN. La politique de preuve
/// (`ProofPolicy`) décide à la création ; ce champ suit sa décision, il ne la
/// refait pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </param>
public sealed record DeliveryPickedUpDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId,
    string? IssuedPin) : DomainEvent;

/// <summary>
/// Remise effectuée. C'est cet événement qui déclenche le gain du livreur.
///
/// Il PORTE le montant plutôt que de laisser le consommateur le recalculer : le
/// taux de partage bougera, et un service de paie qui referait le calcul avec le
/// taux du jour paierait les courses anciennes au tarif nouveau.
/// </summary>
public sealed record DeliveryCompletedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId,
    DateTime DeliveredAtUtc,
    decimal? DriverEarning,
    string? Currency) : DomainEvent;

/// <summary>Course annulée.</summary>
public sealed record DeliveryCancelledDomainEvent(Guid DeliveryId, string Reference, DeliverySource Source, string? Reason) : DomainEvent;

/// <summary>
/// Aucun livreur n'a répondu après épuisement des tentatives.
///
/// Destiné au DISPATCH HUMAIN, pas au client : la course reste vivante et
/// reprenable. C'est un appel à l'aide, pas un constat d'échec.
/// </summary>
public sealed record DeliveryNoDriverAvailableDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    int Attempts) : DomainEvent;
