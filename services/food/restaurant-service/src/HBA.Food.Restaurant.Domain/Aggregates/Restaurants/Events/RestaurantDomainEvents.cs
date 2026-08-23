using HBA.Shared.Domain.Events;

namespace HBA.Food.Domain.Restaurants.Events;

/// <summary>Un établissement vient d'être créé (Draft : invisible des clients).</summary>
public sealed record RestaurantRegisteredDomainEvent(Guid RestaurantId, Guid OwnerUserId, string Name) : DomainEvent;

/// <summary>
/// HBA a validé l'établissement.
///
/// C'EST ICI QUE LE RÔLE « FoodPartner » DOIT ÊTRE ATTRIBUÉ, et pas à la
/// création. Créer un établissement est une CANDIDATURE, ouverte à tout compte ;
/// donner le rôle à ce moment reviendrait à laisser chacun se décerner sa propre
/// habilitation. La validation, elle, est la décision d'un humain qui a regardé
/// un dossier. Même raisonnement que pour les livreurs.
/// </summary>
/// <remarks>
/// Le NOM voyage : les notifications s'adressent à un établissement nommé, pas à
/// un identifiant. Le relire depuis un handler aurait demandé un dépôt de lecture
/// dédié — beaucoup de mécanique pour un libellé que l'agrégat a sous la main.
/// </remarks>
public sealed record RestaurantApprovedDomainEvent(Guid RestaurantId, Guid OwnerUserId, string Name) : DomainEvent;

/// <summary>
/// Dossier refusé. Le motif voyage : sans lui, le restaurateur resoumet le même
/// dossier et la modération le refuse à nouveau.
/// </summary>
public sealed record RestaurantRejectedDomainEvent(Guid RestaurantId, Guid OwnerUserId, string? Reason) : DomainEvent;

/// <summary>L'établissement est écarté par la plateforme : son menu quitte la vitrine.</summary>
public sealed record RestaurantSuspendedDomainEvent(Guid RestaurantId, Guid OwnerUserId, string? Reason) : DomainEvent;

/// <summary>La suspension est levée : l'établissement redevient visible.</summary>
public sealed record RestaurantReopenedDomainEvent(Guid RestaurantId, Guid OwnerUserId) : DomainEvent;

/// <summary>Le restaurateur a quitté la plateforme.</summary>
public sealed record RestaurantClosedDomainEvent(Guid RestaurantId, Guid OwnerUserId) : DomainEvent;
