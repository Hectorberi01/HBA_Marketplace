using HBA.Shared.Domain.Events;

namespace HBA.FoodCarts.Domain.Carts.Events;

/// <summary>Un panier de restauration a été ouvert pour un acheteur.</summary>
public sealed record FoodCartCreatedDomainEvent(Guid CartId, Guid BuyerId) : DomainEvent;

/// <summary>Un plat a été ajouté au panier.</summary>
public sealed record FoodItemAddedToCartDomainEvent(
    Guid CartId, Guid RestaurantId, Guid MenuItemId, int Quantity) : DomainEvent;

/// <summary>
/// Le panier a été validé.
///
/// IL PORTE LE RESTAURANT, LÀ OÙ SON ANCÊTRE NE PORTAIT QUE L'ACHETEUR.
///
/// `CartCheckedOutDomainEvent` n'avait aucun consommateur — le service de
/// commande lisait le panier par gRPC au lieu d'écouter. Celui-ci garde la même
/// forme mais dit d'emblée DE QUEL établissement il s'agit : c'est l'information
/// qu'un consommateur devrait sinon aller rechercher, et la seule qui ne se
/// déduit pas des lignes une fois le panier clos.
/// </summary>
public sealed record FoodCartCheckedOutDomainEvent(
    Guid CartId, Guid BuyerId, Guid RestaurantId) : DomainEvent;
