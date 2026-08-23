using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Contracts.IntegrationEvents;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ÉVÉNEMENTS DE COMMANDE ET DE CUISINE, HORS DU MODULE (cahier §19).
///
/// SANS EUX, TOUT LE MODULE DE COMMANDE EST MUET.
///
/// L'agrégat levait déjà ses événements de domaine — ils ne sortaient nulle part.
/// Un restaurateur ne recevait aucune notification de commande, Ordering ne savait
/// jamais qu'un restaurant avait refusé, et surtout : AUCUN LIVREUR N'ÉTAIT APPELÉ
/// quand un sac était prêt. Des repas auraient refroidi sur un passe sans que
/// personne ne vienne, et rien dans les journaux ne l'aurait expliqué.
///
/// LE CAHIER LES NOMME EN SUJETS KAFKA. Ils passent ici par l'outbox
/// transactionnel : même garantie « au moins une fois », sans second système à
/// exploiter, et le jour de l'extraction seul l'expéditeur change.
///
/// TOUS PORTENT L'<c>OrderId</c> EN PLUS DU <c>FoodOrderId</c>. Sans lui, un
/// consommateur devrait rappeler Food pour savoir de quelle commande commerciale
/// il s'agit — et un événement qui oblige à rappeler son émetteur n'est qu'une
/// notification déguisée.
///
/// ET TOUS PORTENT DÉSORMAIS <c>OrderOrigin</c> : SANS LUI, <c>OrderId</c> NE
/// DÉSIGNAIT RIEN.
///
/// Voir <see cref="FoodOrderOrigins"/>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class FoodOrderOrigins
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DE QUEL UNIVERS VIENT L'<c>OrderId</c> PORTÉ PAR CES ÉVÉNEMENTS.
    ///
    /// ÉCRIT PARCE QUE SIX GESTIONNAIRES LISAIENT `OrderId` NU.
    ///
    /// Deux ponts ouvrent un ticket de cuisine : une commande order-service dont
    /// une ligne est un plat, ou une `MealOrder` de food-order-service. Les deux
    /// écrivaient le même champ. Chaque consommateur interrogeait ensuite SA base
    /// avec — et pour chaque ticket, l'un des deux jeux de consommateurs
    /// travaillait forcément sur un identifiant étranger.
    ///
    /// Le plus coûteux : la création de course demandait l'adresse de livraison à
    /// order-service. Pour un ticket né d'une `MealOrder`, « commande
    /// introuvable » — donc aucune course, jamais, sur un repas déjà prêt.
    ///
    /// UNE CHAÎNE, PAS L'ÉNUMÉRATION DU DOMAINE.
    ///
    /// `FoodOrderOrigin` vit dans `HBA.Food.Restaurant.Domain`. Ce projet de
    /// contrats ne référence que le socle, et doit le rester : un consommateur
    /// qui devrait tirer le domaine de restaurant-service pour lire un événement
    /// ferait exactement la dépendance que la frontière du module interdit —
    /// c'est le même raisonnement que `Reason`, qui voyage en chaîne pour ne pas
    /// exporter `FoodRejectionReason`.
    ///
    /// COMPARER AVEC `OrdinalIgnoreCase`, JAMAIS AVEC `==`.
    ///
    /// La valeur traverse Kafka en JSON. Un producteur d'une autre version peut
    /// écrire « FOOD » ou « food » ; un filtre sensible à la casse se tairait,
    /// et un filtre qui se tait ressemble à un filtre qui fonctionne.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public const string Marketplace = "Marketplace";

    public const string Food = "Food";
}
public sealed record FoodOrderReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required decimal Total { get; init; }
    public required int ItemCount { get; init; }

    /// <summary>
    /// L'univers de <see cref="FoodOrderReceivedIntegrationEvent.OrderId"/> —
    /// <see cref="FoodOrderOrigins"/>.
    ///
    /// OPTIONNEL, AVEC « Marketplace » PAR DÉFAUT (D32). Les messages déjà
    /// écrits dans les outbox et les sujets Kafka ne portent pas ce champ, et ils
    /// viennent tous de la marketplace : le défaut les décrit exactement. Le
    /// rendre `required` ferait échouer la désérialisation de tout ce qui est
    /// encore en file le jour du déploiement.
    /// </summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>
/// Le restaurant a accepté. Vaut aussi <c>kitchen.ticket.created</c>.
///
/// <c>EstimatedPreparationMinutes</c> voyage avec : c'est la promesse faite au
/// client, et l'heure de livraison affichée en dépend.
/// </summary>
public sealed record FoodOrderAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required int EstimatedPreparationMinutes { get; init; }

    /// <summary>
    /// NUL quand l'acceptation est AUTOMATIQUE (§3). « Personne, et c'était
    /// voulu » n'est pas « on ne sait pas qui ».
    /// </summary>
    public Guid? AcceptedByUserId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>
/// Le restaurant a refusé.
///
/// C'EST L'ÉVÉNEMENT LE PLUS URGENT DE CETTE FAMILLE : le client a payé. Un
/// refus qui ne remonterait pas à Ordering et Payments laisserait un débit sans
/// contrepartie, et le client découvrirait tout seul qu'il n'aura pas de repas.
///
/// Le motif voyage en CHAÎNE, jamais en énumération : un consommateur qui devrait
/// référencer <c>FoodRejectionReason</c> ferait la dépendance que la frontière du
/// module interdit.
/// </summary>
public sealed record FoodOrderRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string Reason { get; init; }
    public string? Comment { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>La cuisine a commencé. Vaut <c>kitchen.ticket.started</c>.</summary>
public sealed record FoodOrderPreparingIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SAC EST PRÊT — L'ÉVÉNEMENT QUI APPELLE UN LIVREUR.
///
/// Le §24 le place au centre du flux : ReadyForPickup → HBA Delivery → HBA
/// Driver. C'est le seul de cette famille dont l'absence se verrait tout de
/// suite, et le raccordement à Delivery est le prochain jalon du module.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record FoodOrderReadyForPickupIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required DateTime ReadyAtUtc { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

public sealed record FoodOrderPickedUpIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE REPAS EST REMIS AU CLIENT — L'ÉVÉNEMENT QUI FAIT PAYER LE RESTAURATEUR.
///
/// IL N'EXISTAIT PAS, ET C'EST CE QUI BLOQUAIT L'ARGENT.
///
/// Le ticket passait « livré » dans la base de Food et s'arrêtait là. La commande
/// commerciale restait « confirmée », <c>OrderDelivered</c> n'était donc jamais
/// publié, l'escrow n'était jamais levé, et le gain du restaurateur — comptabilisé
/// dès la confirmation, en « à venir » — n'était jamais libéré. Le repas était
/// remis au client et le restaurateur n'était jamais payé.
///
/// Son unique consommateur est order-service, qui seul connaît la commande
/// commerciale : <c>OrderId</c> voyage donc avec, comme sur toute cette famille.
/// Sans lui, le consommateur devrait rappeler Food, et un événement qui oblige à
/// rappeler son émetteur n'est qu'une notification déguisée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record FoodOrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>
/// Annulée.
///
/// <c>WasInKitchen</c> porte la seule chose qui compte pour le restaurant : des
/// denrées avaient-elles été engagées ? Une annulation avant acceptation ne coûte
/// rien ; la même trente minutes plus tard coûte un repas, et c'est ce qui fonde
/// une éventuelle indemnisation.
/// </summary>
public sealed record FoodOrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public string? Reason { get; init; }
    public required bool WasInKitchen { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>. Optionnel (D32).</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}
