using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Deliveries.Domain.Deliveries.Events;
using HBA.Deliveries.Domain.Drivers.Events;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Deliveries.Application.Deliveries.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TRADUCTION DES FAITS INTERNES EN FAITS PUBLICS.
///
/// TOUS LES ÉVÉNEMENTS DE DOMAINE NE SORTENT PAS.
///
/// Ceux du dispatch — refus, relance de la recherche — restent à l'intérieur du
/// module : ils décrivent une mécanique, pas un fait sur lequel un tiers agit.
/// Les publier produirait plusieurs messages par course, que l'outbox devrait
/// écrire, relayer et conserver, et que personne ne consomme.
///
/// LA PROPOSITION FAIT EXCEPTION, ET CETTE RÈGLE A DÛ ÊTRE CORRIGÉE.
///
/// « Proposition faite » figurait dans la liste des faits internes. C'était une
/// erreur de raisonnement : la règle disait « ne sortent que les faits
/// actionnables par un tiers », et j'avais lu « tiers » comme « partenaire
/// marchand ». Or il y a un autre tiers, et c'est le plus important de tous — LE
/// LIVREUR. Son compte vit dans Identity, son jeton de notification dans
/// Notifications : il est hors du module, par construction.
///
/// Conséquence de l'omission : l'événement était levé, aucun handler ne
/// l'écoutait, et le dispatch proposait des courses à des gens qui n'en
/// entendaient jamais parler. Le module entier était inerte pour cette raison.
///
/// Ne sortent donc que les faits actionnables : créée, PROPOSÉE, acceptée,
/// collectée, livrée, annulée, et l'appel au dispatch humain.
///
/// CHAQUE TRADUCTEUR EST UNE SIMPLE RECOPIE, ET C'EST VOULU.
///
/// Une première version faisait relire la course en base pour retrouver sa
/// référence, que certains événements de domaine ne portaient pas. C'était une
/// lecture de plus par événement, et surtout un mode de défaillance nouveau sur
/// un chemin où l'échec signifie « le client ne sera jamais prévenu ». Les
/// événements de domaine portent désormais leur référence : il ne reste ici
/// aucune dépendance, aucun appel, rien qui puisse échouer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryCreatedDomainEventHandler : IDomainEventHandler<DeliveryCreatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCreatedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCreatedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Type = domainEvent.Type.ToString()
            },
            cancellationToken);
}

/// <summary>
/// Une course vient d'être proposée à un livreur. Il a quarante-cinq secondes
/// pour répondre, et il faut donc le prévenir MAINTENANT.
///
/// LE DÉLAI EST SERRÉ, ET LE CHEMIN N'EST PAS INSTANTANÉ.
///
/// L'outbox scrute toutes les cinq secondes : la notification part donc entre
/// zéro et cinq secondes après la proposition, laissant au livreur une quarantaine
/// de secondes utiles. C'est suffisant, mais ce n'est pas confortable — et c'est
/// pourquoi la poussée n'est PAS le seul chemin. <c>GET /api/deliveries/mine</c>
/// montre la proposition à tout moment, sans dépendre d'un jeton de notification
/// valide ni d'un téléphone sorti de veille.
///
/// Une notification perdue coûte alors une course reproposée à quelqu'un d'autre.
/// Une notification qui n'existe pas, comme c'était le cas, coûte la totalité des
/// courses.
/// </summary>
public sealed class DeliveryAssignedDomainEventHandler : IDomainEventHandler<DeliveryAssignedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryAssignedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryAssignedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryAssignedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                DriverId = domainEvent.DriverId
            },
            cancellationToken);
}

/// <summary>
/// L'exploitation a vérifié les pièces d'un livreur : il est autorisé à travailler.
///
/// Le consommateur est le composition root, qui attribue le rôle « Driver » côté
/// Identity. Ce module ne connaît pas Identity, et ne doit pas le connaître.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE TYPE PUBLIÉ VIENT DE `HBA.Drivers.Contracts`, PLUS DE `HBA.Deliveries.Contracts`.
///
/// `DriverVerifiedIntegrationEvent` existait dans LES DEUX, aux champs identiques.
/// `KafkaEventNaming.EventType` ne regarde que le nom de CLASSE et l'enveloppe
/// Kafka ne transporte que ce nom : les deux rendaient « driver.verified », et
/// `ResolveEventType` retenait le premier par ordre alphabétique du nom complet.
///
/// CE QUE ÇA PROVOQUAIT : un gestionnaire enregistré pour l'AUTRE type n'était
/// JAMAIS appelé — sans exception, sans échec de désérialisation, avec l'offset
/// committé juste après. Ici les deux côtés se trouvaient d'accord par hasard sur
/// la version Deliveries ; il suffisait qu'un service référence Drivers et pas
/// Deliveries — ce qui est le cas naturel pour qui écoute les livreurs — pour que
/// le rôle `Driver` cesse d'être attribué en silence, et qu'un livreur vérifié se
/// fasse refouler en 403 sans qu'aucun journal ne relie le refus à sa
/// vérification.
///
/// POURQUOI DRIVERS : l'agrégat décrit est LE LIVREUR, pas la course. L'événement
/// siège à côté de `driver.created` et `driver.suspended`. Ce module en reste le
/// PRODUCTEUR — c'est lui qui détient la vérification — mais il publie le contrat
/// du domaine auquel le fait appartient.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DriverVerifiedDomainEventHandler : IDomainEventHandler<DriverVerifiedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DriverVerifiedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DriverVerifiedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DriverVerifiedIntegrationEvent
            {
                DriverId = domainEvent.DriverId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Un livreur a accepté : le client peut être prévenu.</summary>
public sealed class DeliveryAcceptedDomainEventHandler : IDomainEventHandler<DeliveryAcceptedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryAcceptedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryAcceptedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryAcceptedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId
            },
            cancellationToken);
}

/// <summary>
/// Colis pris en charge : la course est physiquement engagée.
///
/// C'EST ICI QUE LE CODE DE REMISE PART VERS LE DESTINATAIRE.
///
/// `Delivery` tirait un PIN à la création et `ProofOfDelivery.Capture` le
/// vérifiait à la remise, mais aucun maillon ne le portait au client : le
/// livreur réclamait un code que personne n'avait reçu. Ce gestionnaire est ce
/// maillon — il chiffre le code et le joint à l'événement public, que
/// communication-service déchiffre pour composer la notification.
///
/// CHIFFRÉ AVANT DE SORTIR, JAMAIS APRÈS.
///
/// La charge écrite dans `outbox_messages.Content` puis publiée sur Kafka doit
/// déjà être illisible : c'est cette table et ce topic qu'on protège, pas le
/// transport. Chiffrer plus tard reviendrait à ne pas chiffrer.
/// </summary>
public sealed class DeliveryPickedUpDomainEventHandler : IDomainEventHandler<DeliveryPickedUpDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ISecretProtector _protecteur;

    public DeliveryPickedUpDomainEventHandler(
        IIntegrationEventPublisher publisher, ISecretProtector protecteur)
    {
        _publisher = publisher;
        _protecteur = protecteur;
    }

    public Task HandleAsync(DeliveryPickedUpDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryPickedUpIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId,

                // NUL quand la course ne demande pas de code — photo, signature, ou
                // aucune preuve. On ne chiffre pas « rien » pour produire une charge
                // que le consommateur devrait ensuite apprendre à ignorer.
                ProtectedDeliveryPin = domainEvent.IssuedPin is { Length: > 0 } code
                    ? _protecteur.Protect(code)
                    : null
            },
            cancellationToken);
}

/// <summary>
/// Remise effectuée — l'événement le plus consommé du module : il clôt la
/// commande, déclenche le gain du livreur et part en webhook partenaire.
/// </summary>
public sealed class DeliveryCompletedDomainEventHandler : IDomainEventHandler<DeliveryCompletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCompletedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCompletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCompletedIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                DriverId = domainEvent.DriverId,
                DeliveredAtUtc = domainEvent.DeliveredAtUtc,
                DriverEarning = domainEvent.DriverEarning,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>Annulation avant collecte.</summary>
public sealed class DeliveryCancelledDomainEventHandler : IDomainEventHandler<DeliveryCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(DeliveryCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryCancelledIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Aucun livreur trouvé : appel au dispatch humain, pas au client final.</summary>
public sealed class DeliveryNoDriverAvailableDomainEventHandler
    : IDomainEventHandler<DeliveryNoDriverAvailableDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public DeliveryNoDriverAvailableDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        DeliveryNoDriverAvailableDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new DeliveryNoDriverAvailableIntegrationEvent
            {
                DeliveryId = domainEvent.DeliveryId,
                Reference = domainEvent.Reference,
                Source = domainEvent.Source.ToString(),
                Attempts = domainEvent.Attempts
            },
            cancellationToken);
}
