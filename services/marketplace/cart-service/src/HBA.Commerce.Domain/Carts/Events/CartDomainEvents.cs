using HBA.Shared.Domain.Events;

namespace HBA.Commerce.Domain.Carts.Events;

/// <summary>Un panier a été créé pour un acheteur.</summary>
public sealed record CartCreatedDomainEvent(Guid CartId, Guid BuyerId) : DomainEvent;

/// <summary>
/// Un article a été ajouté au panier.
///
/// <paramref name="ItemId"/> S'APPELAIT `OfferId`, ET LE NOM SERAIT DEVENU FAUX.
///
/// Depuis que le panier porte des plats, cet identifiant désigne soit une offre
/// marketplace, soit un article de carte. Le laisser s'appeler « offre » aurait
/// conduit tout consommateur futur à chercher une offre qui n'existe pas — d'où
/// <paramref name="Kind"/>, qui dit lequel des deux on regarde.
///
/// Le renommage était gratuit : au moment où il a été fait, aucun handler
/// n'écoutait cet événement. Il ne l'aurait plus été six mois plus tard.
/// </summary>
public sealed record ItemAddedToCartDomainEvent(
    Guid CartId, Guid ItemId, int Quantity, string Kind) : DomainEvent;

/// <summary>Le panier a été validé (checkout) — consommé par Ordering.</summary>
public sealed record CartCheckedOutDomainEvent(Guid CartId, Guid BuyerId) : DomainEvent;
