using HBA.Shared.IntegrationEvents;

namespace HBA.Deliveries.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES FAITS QUE DELIVERIES PUBLIE AU RESTE DU SYSTÈME.
///
/// Ils portent la RÉFÉRENCE et la SOURCE, jamais un identifiant de commande typé.
/// C'est ce qui permet au même événement de servir trois consommateurs très
/// différents :
///
///   • Ordering, qui reconnaît sa propre référence et fait avancer la commande ;
///   • Wallet, qui crédite le livreur ;
///   • le service de webhooks, qui prévient un partenaire externe dont nous ne
///     savons rien d'autre que cette référence.
///
/// Un événement qui exposerait un « OrderId » forcerait Delivery à connaître
/// Ordering — exactement la dépendance que le moteur logistique doit refuser
/// pour rester vendable à des tiers.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record DeliveryCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required string Type { get; init; }
}

/// <summary>
/// Une course est PROPOSÉE à un livreur, qui a quarante-cinq secondes pour
/// répondre.
///
/// NE PORTE PAS LE UserId DU LIVREUR, ALORS QUE SON SEUL CONSOMMATEUR EN A
/// BESOIN.
///
/// Le composition root envoie une notification poussée, et Notifications
/// s'adresse à un compte utilisateur, pas à un livreur. La tentation était donc
/// d'ajouter le champ ici. Elle a été écartée : un événement d'intégration élargi
/// pour le confort d'un consommateur devient un engagement envers tous les
/// autres, présents et futurs — et celui-ci part aussi vers l'API partenaires, à
/// qui le compte utilisateur d'un livreur HBA ne regarde pas.
///
/// Le handler relit donc le livreur par <c>IDeliveryModuleApi</c>. C'est le même
/// arbitrage que pour la création de profil à l'inscription.
/// </summary>
public sealed record DeliveryAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
}

// ═════════════════════════════════════════════════════════════════════════════
// `DriverVerifiedIntegrationEvent` A ÉTÉ RETIRÉ D'ICI. NE PAS LE REMETTRE.
//
// Il était déclaré une seconde fois, aux champs strictement identiques
// (`DriverId`, `UserId`), dans `HBA.Drivers.Contracts.IntegrationEvents`. Or
// `KafkaEventNaming.EventType` ne regarde que le NOM DE CLASSE, et l'enveloppe
// Kafka ne transporte que ce nom, jamais l'espace de noms : les deux rendaient
// « driver.verified », indiscernables à la réception.
//
// CE QUE LA COLLISION PROVOQUAIT : `ResolveEventType` voyait deux types répondre
// et retenait LE PREMIER PAR ORDRE ALPHABÉTIQUE du nom complet — « HBA.Deliveries… »
// avant « HBA.Drivers… », donc celui d'ici. Un gestionnaire enregistré pour
// l'AUTRE type n'était JAMAIS appelé : pas d'exception, pas d'échec de
// désérialisation, un avertissement noyé au démarrage, et l'offset committé juste
// après. Le rôle `Driver` n'aurait pas été attribué, et le BFF livreur aurait
// répondu 403 à un livreur pourtant vérifié, sans qu'aucun journal ne relie le
// refus à la vérification.
//
// POURQUOI C'EST DRIVERS QUI EST RETENU : le sujet de l'événement est LE LIVREUR,
// pas la course. Un événement appartient au domaine de l'agrégat qu'il DÉCRIT, pas
// au service qui l'émet — et `driver.verified` siège naturellement à côté de
// `driver.created`, `driver.suspended` et `driver.availability-changed`, qui vivent
// tous dans `HBA.Drivers.Contracts`. Le producteur de ce module
// (`DriverVerifiedDomainEventHandler`) et son consommateur côté identity
// (`GrantDriverRoleHandler`) publient et écoutent désormais ce type-là.
//
// Le raisonnement sur le `UserId` qui accompagnait cette déclaration reste valable
// et vaut pour la version conservée : le fait publié EST « ce compte utilisateur
// est désormais un livreur agréé ». Sans lui, l'événement n'aurait aucun
// consommateur possible hors du module.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un livreur a accepté la course. Le client peut être prévenu.</summary>
public sealed record DeliveryAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
}

/// <summary>Le colis est pris en charge : la course est physiquement engagée.</summary>
public sealed record DeliveryPickedUpIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }

    /// <summary>
    /// Code de remise à porter au DESTINATAIRE, chiffré (AES-GCM, `ISecretProtector`).
    /// NUL quand la course ne demande pas de code (photo, signature, ou aucune preuve).
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// ÉCRIT PARCE QUE LE CODE ÉTAIT VÉRIFIÉ MAIS JAMAIS DÉLIVRÉ.
    ///
    /// `Delivery` tirait bien un PIN à la création, `ProofOfDelivery.Capture` le
    /// comparait bien à la remise — et RIEN, entre les deux, ne le portait au
    /// client. Le livreur arrivait, demandait un code que le destinataire n'avait
    /// jamais reçu, et la course ne pouvait pas se clore. Le contrôle était
    /// correct et inapplicable : un code que le destinataire ne connaît pas ne
    /// prouve rien, il bloque.
    ///
    /// AJOUT OPTIONNEL, PAS `required` (D32).
    ///
    /// Les consommateurs déjà déployés (webhooks partenaires, restaurant-service)
    /// désérialisent l'événement sans ce champ. Le rendre obligatoire les aurait
    /// tous cassés pour un champ dont ils n'ont aucun usage.
    ///
    /// CHIFFRÉ, PARCE QU'IL TRAVERSE L'OUTBOX ET KAFKA.
    ///
    /// Même raisonnement que les codes de réinitialisation : la charge reste sept
    /// jours sur le topic et indéfiniment dans `outbox_messages.Content`. Un accès
    /// en LECTURE à l'un des deux suffirait sinon à réceptionner le colis d'un
    /// autre — le PIN EST le justificatif de remise.
    ///
    /// CE QUE CE CHAMP NE COUVRE PAS.
    ///
    /// Il est émis À LA COLLECTE, pas à la création : avant que le colis parte, le
    /// code n'a aucune raison d'être connu. Et si la notification échoue, il
    /// n'existe AUCUNE seconde chance automatique — le destinataire doit passer
    /// par le support, qui relit le code en base. Un renvoi à la demande reste à
    /// écrire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? ProtectedDeliveryPin { get; init; }
}

/// <summary>
/// Remise effectuée.
///
/// C'est l'événement le plus consommé du module : il clôt la commande côté
/// Ordering, déclenche le gain du livreur côté Wallet, et part en webhook chez
/// le partenaire. Toute modification de sa forme est une rupture de contrat
/// EXTERNE — pas seulement interne.
/// </summary>
public sealed record DeliveryCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
    public required DateTime DeliveredAtUtc { get; init; }

    /// <summary>
    /// Part revenant au livreur, figée à la remise. NULLE quand la course n'avait
    /// aucun prix — ce qui n'est pas « zéro » : « aucun gain calculé » se cherche,
    /// « zéro franc » se paie.
    /// </summary>
    public decimal? DriverEarning { get; init; }

    public string? Currency { get; init; }
}

/// <summary>Course annulée avant collecte.</summary>
public sealed record DeliveryCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Aucun livreur trouvé après épuisement des tentatives.
///
/// Destiné au DISPATCH HUMAIN et au donneur d'ordre — pas au client final. La
/// course reste vivante et reprenable : annoncer un échec à l'acheteur alors
/// qu'un opérateur peut encore la pourvoir ferait perdre une vente pour rien.
/// </summary>
public sealed record DeliveryNoDriverAvailableIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required int Attempts { get; init; }
}
