// DÉPLACÉ DEPUIS `driver-service/src/HBA.Delivery.Driver.Domain/Events`
// (lot 5.4, ISSUE-069). L'événement est levé par l'agrégat `Driver` et consommé
// par `DriverVerifiedDomainEventHandler`, tous deux dans delivery-service.

using HBA.Shared.Domain.Events;

namespace HBA.Deliveries.Domain.Drivers.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'EXPLOITATION A VÉRIFIÉ LES PIÈCES D'UN LIVREUR.
///
/// C'est le seul moment où quelqu'un chez HBA atteste qu'une personne a le droit
/// de transporter les colis des clients. L'inscription, elle, ne prouve rien :
/// n'importe quel compte peut se déclarer livreur.
///
/// PORTE LE UserId EN PLUS DU DriverId.
///
/// Le consommateur utile de cet événement est le composition root, qui attribue
/// le rôle « Driver » côté Identity. Identity ne connaît que le compte. Sans le
/// <c>UserId</c> ici, le handler devrait relire le livreur pour l'obtenir —
/// une lecture de plus sur un fait que l'agrégat tient déjà en main.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record DriverVerifiedDomainEvent(Guid DriverId, Guid UserId) : DomainEvent;
