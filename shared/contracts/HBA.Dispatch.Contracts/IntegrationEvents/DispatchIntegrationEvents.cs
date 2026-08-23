using HBA.Shared.IntegrationEvents;

namespace HBA.Dispatch.Contracts.IntegrationEvents;

[HbaEvent("dispatch.started", Version = 1, AggregateType = "DispatchJob")]
public sealed record DispatchStartedIntegrationEvent : IntegrationEvent
{
    public required Guid DispatchJobId { get; init; }
    public required Guid DeliveryId { get; init; }
}

[HbaEvent("dispatch.offer-created", Version = 1, AggregateType = "DeliveryOffer")]
public sealed record DispatchOfferCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
}

[HbaEvent("dispatch.offer-expired", Version = 1, AggregateType = "DeliveryOffer")]
public sealed record DispatchOfferExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
}

[HbaEvent("dispatch.no-driver-found", Version = 1, AggregateType = "DispatchJob")]
public sealed record DispatchNoDriverFoundIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required int Attempts { get; init; }
}

// ═════════════════════════════════════════════════════════════════════════════
// `DeliveryAssignedIntegrationEvent` A ÉTÉ RETIRÉ D'ICI. NE PAS LE REMETTRE.
//
// Il était déclaré une seconde fois, à l'identique (`DeliveryId`, `DriverId`),
// dans `HBA.Deliveries.Contracts.IntegrationEvents`. Or `KafkaEventNaming.EventType`
// ne regarde que le NOM DE CLASSE : les deux rendaient « delivery.assigned », et
// l'enveloppe Kafka ne transporte que ce nom, jamais l'espace de noms.
//
// CE QUE LA COLLISION PROVOQUAIT : `ResolveEventType` voyait deux types répondre
// et retenait LE PREMIER PAR ORDRE ALPHABÉTIQUE du nom complet — « HBA.Deliveries… »
// avant « HBA.Dispatch… ». Un gestionnaire enregistré pour l'AUTRE type n'aurait
// JAMAIS été appelé : pas d'exception, pas d'échec de désérialisation, un
// avertissement au démarrage, et l'offset committé juste après. Ici la roulette
// tombait du bon côté par accident — `NotifyDriverOnDeliveryAssignedHandler` écoute
// bien la version Deliveries — mais renommer un espace de noms suffisait à
// l'inverser, et la panne aurait été parfaitement muette.
//
// POURQUOI C'EST DELIVERIES QUI EST RETENU : le sujet de l'événement est LA
// COURSE, agrégat de delivery-service. Dispatch décide de l'affectation, il n'est
// pas propriétaire du fait — un événement appartient au domaine de l'agrégat
// qu'il DÉCRIT, pas au service qui l'émet. `DispatchStore` publie donc désormais
// le type de `HBA.Deliveries.Contracts` (voir la référence de projet ajoutée à
// `HBA.Delivery.Dispatch.Application.csproj`).
//
// Les événements ci-dessus restent ici : `dispatch.*` décrit bien la mécanique
// d'affectation, dont Dispatch est l'agrégat.
// ═════════════════════════════════════════════════════════════════════════════
