using HBA.Shared.IntegrationEvents;

namespace HBA.FoodOrders.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CES ÉVÉNEMENTS NE S'APPELLENT PAS `OrderPlaced`, `OrderConfirmed`…
///
/// L'ENVELOPPE KAFKA NE PORTE QUE LE NOM COURT DU TYPE.
///
/// Le dépôt a déjà payé cette leçon : un contrat déclaré deux fois — une fois
/// partagé, une fois chez son propriétaire — faisait résoudre le consommateur au
/// hasard de l'ordre de chargement, et un gestionnaire enregistré sur l'autre
/// n'était jamais appelé, sans la moindre erreur. Le commentaire est encore dans
/// `HBA.Commerce.Application.csproj`.
///
/// Deux services publiant chacun un `OrderConfirmedIntegrationEvent` rejoueraient
/// exactement cela, en pire : les charges utiles diffèrent. Le préfixe `MealOrder`
/// rend la collision impossible.
///
/// Le rapprochement des deux familles — un contrat de confirmation commun, émis
/// par les deux services et consommé une seule fois par le paiement, le
/// portefeuille et les notifications — est le lot suivant. Il se fait en
/// DÉPLAÇANT le contrat, pas en le dupliquant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MealOrderPlacedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required Guid CartId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Une ligne figée, telle qu'elle voyage vers la cuisine.</summary>
public sealed record MealOrderLinePayload
{
    public required Guid LineId { get; init; }
    public required Guid MenuItemId { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<MealOrderLineOptionPayload> Options { get; init; } = [];
}

public sealed record MealOrderLineOptionPayload
{
    public required Guid OptionGroupId { get; init; }
    public required Guid OptionId { get; init; }
}

/// <summary>
/// La commande est payée : la cuisine peut la recevoir.
///
/// IL PORTE SES LIGNES, LÀ OÙ L'ANCIEN CHEMIN FAISAIT UN ALLER-RETOUR gRPC.
///
/// `ReceiveFoodOrderOnOrderConfirmedHandler` recevait un événement sans lignes,
/// rappelait order-service par gRPC pour les obtenir, puis filtrait celles dont
/// le `Kind` valait « Food ». Trois pas, dont deux n'existaient que parce que
/// l'événement était commun à deux univers et ne pouvait donc rien porter de
/// spécifique. Ici il n'y a qu'un univers : l'événement dit tout.
/// </summary>
public sealed record MealOrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal ShippingFee { get; init; }
    public required string Currency { get; init; }
    public string? CustomerNote { get; init; }

    /// <summary>Le devis de course figé au paiement. Voir <c>MealOrder.DeliveryQuoteId</c>.</summary>
    public string? DeliveryQuoteId { get; init; }

    public IReadOnlyList<MealOrderLinePayload> Lines { get; init; } = [];
}

public sealed record MealOrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string Reason { get; init; }
}

public sealed record MealOrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
}

/// <summary>
/// La commande est payée mais plus exécutable : elle attend un arbitrage.
///
/// CE N'EST PAS UNE ANNULATION, ET LE CONSOMMATEUR NE DOIT PAS LA TRAITER
/// COMME TELLE.
///
/// La vente est vivante : l'argent est encaissé, et une course annulée est le
/// plus souvent réattribuable. Le seul consommateur attendu est la notification
/// — pour DIRE au client que c'est pris en charge. Un consommateur qui
/// rembourserait ici détruirait des ventes récupérables, sans retour possible.
///
/// <c>Reason</c> voyage en clair : il finit dans la file d'arbitrage, lue par
/// quelqu'un qui ne connaît pas ce code.
/// </summary>
public sealed record MealOrderUnderReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// L'arbitrage a conclu à la REPRISE.
///
/// DISTINCT DE LA CONFIRMATION, ET IL NE FAUT SURTOUT PAS LES CONFONDRE. La
/// confirmation ouvre le ticket de cuisine, décompte le coupon et comptabilise
/// les gains — tout cela a déjà eu lieu. Celui-ci ne dit qu'une chose : la
/// suspension est levée.
/// </summary>
public sealed record MealOrderResumedAfterReviewIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string PreviousReason { get; init; }
}
