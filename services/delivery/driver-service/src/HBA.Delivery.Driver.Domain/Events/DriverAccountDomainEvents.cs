using HBA.Delivery.Driver.Domain.Enums;
using HBA.Shared.Domain.Events;

namespace HBA.Delivery.Driver.Domain.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS FAITS DU DOSSIER LIVREUR.
///
/// CE SONT DES ÉVÉNEMENTS DE DOMAINE, PAS D'INTÉGRATION. Ils ne sortent pas du
/// processus : `DriverDbContext` les dispatche dans la transaction, et ce sont
/// leurs gestionnaires — en couche Application — qui décident de mettre un
/// événement d'INTÉGRATION dans la file, laquelle est drainée vers l'outbox du
/// même `SaveChanges`. C'est ce double étage qui garantit qu'un fait publié
/// correspond toujours à un fait persisté.
///
/// C'est exactement ce qui manquait avant ce lot : `DriverStore` appelait
/// `PublishAsync` directement, sur une file scopée que rien ne drainait, sans
/// aucune écriture en face (ISSUE-007).
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record DriverAccountRegisteredDomainEvent(Guid DriverId, Guid UserId) : DomainEvent;

/// <summary>
/// L'exploitation a vérifié un dossier.
///
/// PORTE L'IDENTITÉ COMPLÈTE, ET C'EST DÉLIBÉRÉ. Son consommateur final est
/// delivery-service, qui doit CRÉER sa projection dispatchable — donc appeler
/// `Driver.Register(userId, fullName, phone, vehicle)`. Un événement réduit aux
/// identifiants l'obligerait à rappeler ce service par gRPC juste après, et
/// l'échec de cet appel laisserait un livreur vérifié que le dispatch ignore,
/// sans que rien ne le signale.
/// </summary>
public sealed record DriverAccountVerifiedDomainEvent(
    Guid DriverId,
    Guid UserId,
    string FullName,
    string Phone,
    DriverVehicleType Vehicle) : DomainEvent;

public sealed record DriverAccountSuspendedDomainEvent(Guid DriverId, Guid UserId, string? Reason) : DomainEvent;

/// <summary>
/// Un véhicule vient d'être déclaré ou remplacé.
///
/// C'EST L'UN DES DEUX ÉVÉNEMENTS QUE CE SERVICE PERDAIT INTÉGRALEMENT
/// (ISSUE-007) : `DriverStore.AddVehicleAsync` appelait `PublishAsync` sur une
/// file scopée qu'aucun `DbContext` ne drainait. Il passe désormais par l'outbox
/// du module, dans la transaction qui écrit le véhicule.
/// </summary>
public sealed record DriverVehicleDeclaredDomainEvent(
    Guid DriverId, Guid VehicleId, DriverVehicleType Vehicle) : DomainEvent;
