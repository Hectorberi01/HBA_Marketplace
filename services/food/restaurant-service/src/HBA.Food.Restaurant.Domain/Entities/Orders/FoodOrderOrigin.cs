namespace HBA.Food.Domain.Orders;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// DE QUEL UNIVERS VIENT LA COMMANDE QUE CE TICKET PRÉPARE.
///
/// ÉCRIT PARCE QUE `FoodOrder.OrderId` PORTAIT DEUX CHOSES DIFFÉRENTES.
///
/// Deux ponts ouvrent un ticket de cuisine :
///
///   • `ReceiveFoodOrderOnOrderConfirmedHandler` — une commande order-service
///     dont une ligne est un plat (`OrderLineKind.Food`) ;
///   • `ReceiveFoodOrderOnMealOrderConfirmedHandler` — une `MealOrder` de
///     food-order-service.
///
/// Les deux écrivaient dans le MÊME champ `OrderId`, sans rien pour les
/// distinguer. Le commentaire de ce champ disait encore « la commande
/// commerciale, chez Ordering » — vrai pour la moitié des tickets.
///
/// CE N'EST PAS UNE PRÉCAUTION : SIX GESTIONNAIRES S'EN SERVAIENT NU.
///
///   • `CreateDeliveryOnFoodOrderReadyHandler` demandait l'adresse de livraison
///     à order-service. Pour un ticket né d'une `MealOrder`, la commande était
///     « introuvable » : le gestionnaire levait, les reprises Kafka
///     s'épuisaient, et AUCUNE COURSE N'ÉTAIT JAMAIS CRÉÉE. Le repas était prêt
///     et personne ne cherchait de livreur.
///   • `FoodOrderReadyNotificationHandler` échouait de la même façon, en plus
///     discret : le client du nouveau parcours ne recevait aucun suivi.
///   • `HoldOrderOnDeliveryCancelledHandler` envoyait un identifiant de
///     `MealOrder` à order-service pour la mise en arbitrage.
///   • `CancelOrderOnFoodOrderRejected`, `CancelOrderOnFoodOrderCancelled` et
///     `MarkOrderDeliveredOnFoodOrderDelivered` (order-service) ont chacun un
///     jumeau dans food-order-service. Pour chaque ticket, l'un des deux jeux
///     travaillait forcément sur un identifiant étranger.
///
/// POURQUOI PAS SIMPLEMENT SUPPRIMER L'ANCIEN CHEMIN.
///
/// Sa porte d'entrée est fermée par ailleurs (lot 6.4), mais les tickets DÉJÀ
/// EN BASE en viennent tous. Sans ce champ, on ne pourrait plus dire lesquels —
/// et une commande en cours de préparation le jour du déploiement se
/// retrouverait orpheline. La valeur de reprise est donc `Marketplace`, qui est
/// exacte pour tout ce qui existe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum FoodOrderOrigin
{
    /// <summary>
    /// Une commande d'order-service (`OrderLineKind.Food`).
    ///
    /// VALEUR ZÉRO, DÉLIBÉRÉMENT. Toutes les lignes déjà en base viennent de
    /// là : le défaut de colonne les décrit correctement sans reprise de données.
    /// Le revers est connu — un appelant qui oublie de renseigner l'origine
    /// obtient « Marketplace » en silence. C'est pour cela que
    /// `ReceiveFoodOrderCommand` l'exige SANS valeur par défaut : l'oubli devient
    /// une erreur de compilation, pas un ticket mal classé.
    /// </summary>
    Marketplace = 0,

    /// <summary>Une `MealOrder` de food-order-service.</summary>
    Food = 1
}
