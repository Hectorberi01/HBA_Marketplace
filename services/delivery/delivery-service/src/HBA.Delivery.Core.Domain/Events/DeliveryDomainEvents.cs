using HBA.Shared.Domain.Events;

namespace HBA.Deliveries.Domain.Deliveries.Events;

/// <summary>Faits métier émis par l'agrégat <see cref="Delivery"/>.</summary>
public sealed record DeliveryCreatedDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    DeliveryType Type) : DomainEvent;

/// <summary>Le dispatch a commencé à chercher un livreur.</summary>
public sealed record DeliverySearchingDriverDomainEvent(Guid DeliveryId, int AttemptNumber) : DomainEvent;

/// <summary>Une mission a été proposée à un livreur.</summary>
public sealed record DeliveryAssignedDomainEvent(Guid DeliveryId, Guid DriverId) : DomainEvent;

/// <summary>Le livreur a accepté.</summary>
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
/// LE CODE DE REMISE, PARCE QUE PERSONNE NE LE PORTAIT AU DESTINATAIRE.
/// </param>
public sealed record DeliveryPickedUpDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    Guid DriverId,
    string? IssuedPin) : DomainEvent;

/// <summary>Remise effectuée. C'est cet événement qui déclenche le gain du livreur.</summary>
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

/// <summary>Aucun livreur n'a répondu après épuisement des tentatives.</summary>
public sealed record DeliveryNoDriverAvailableDomainEvent(
    Guid DeliveryId,
    string Reference,
    DeliverySource Source,
    int Attempts) : DomainEvent;
