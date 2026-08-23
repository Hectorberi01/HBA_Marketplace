using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>
/// La référence sous laquelle une commande marketplace se reconnaît dans une
/// course HBA Delivery.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// DELIVERY NE CONNAÎT NI COMMANDE, NI EXPÉDITION, NI VENDEUR.
///
/// C'est le principe qui rend le moteur logistique vendable à des tiers, et il
/// est écrit noir sur blanc dans ses contrats : les événements portent une
/// RÉFÉRENCE opaque et une SOURCE, jamais un identifiant typé. Un
/// `OrderId` sur `DeliveryCompletedIntegrationEvent` obligerait Delivery à
/// connaître Ordering — exactement la dépendance qu'il refuse.
///
/// La chaîne fait donc l'aller-retour sans être interprétée, et c'est ICI, et
/// seulement ici, qu'on sait la relire.
///
/// TROIS PRÉFIXES CIRCULENT SUR LE MÊME CANAL, ET C'EST VOULU.
///
///   • `SHIP-`  posé par Shipping dans le monolithe ;
///   • `FOOD-`  posé par le pont restauration ;
///   • `ORDER-` posé ici.
///
/// Tous les consommateurs reçoivent TOUS les événements de course. Sans préfixe
/// distinct, ce gestionnaire lirait un identifiant de commande restauration
/// comme le sien et enverrait `MarkOrderDelivered` sur un GUID qui n'existe pas
/// dans sa base — un échec silencieux, puisqu'une commande introuvable ne lève
/// pas. La lecture rend donc `null` pour tout ce qui n'est pas à nous, et c'est
/// le cas NORMAL, pas une anomalie.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class OrderDeliveryReference
{
    // DÉLÉGUÉ AU SOCLE PARTAGÉ — voir `DeliveryReference`.
    //
    // Cette classe portait sa propre copie du préfixe et du découpage, comme
    // food-service portait la sienne. Deux façons de fabriquer la même chaîne
    // finissent par diverger, et la divergence se manifeste par des commandes
    // qui n'atteignent jamais « livrée », sans erreur. On garde le nom local
    // pour la lisibilité des appelants ; la convention, elle, est unique.
    public static string For(Guid orderId) => DeliveryReference.ForOrder(orderId);

    public static Guid? Read(string? reference) => DeliveryReference.ReadOrder(reference);
}

/// <summary>
/// Étape finale du Saga : la course est terminée, la commande passe « livrée ».
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// REMPLACE `MarkOrderDeliveredOnAllShipmentsDeliveredHandler`, QUI NE POUVAIT
///    PAS EXISTER ICI.
///
/// L'ancien gestionnaire écoutait `ShipmentDeliveredIntegrationEvent` puis
/// demandait à `IShippingModuleApi` si TOUTES les expéditions de la commande
/// étaient livrées. Or **le module Shipping n'a jamais été extrait** : seul son
/// projet de contrats a été recopié, et aucun service n'implémente l'interface.
/// Le conteneur le disait au démarrage — « Unable to resolve service for type
/// 'IShippingModuleApi' » — et order-service ne démarrait pas du tout.
///
/// CE QUE CE CHANGEMENT COÛTE : LE MULTI-COLIS DISPARAÎT.
///
/// L'ancienne logique gérait une commande éclatée en plusieurs expéditions —
/// plusieurs vendeurs, plusieurs colis — et n'avançait qu'à la DERNIÈRE. Ici,
/// une course terminée suffit. Tant qu'une commande donne lieu à une seule
/// course, les deux se valent ; le jour où l'on découpe une commande en
/// plusieurs livraisons, il faudra compter, et le compteur devra vivre dans
/// order-service puisque personne d'autre ne connaît la commande.
///
/// C'est un choix assumé, pas un oubli : mieux vaut un service qui démarre et
/// clôt les commandes mono-course qu'un service qui ne démarre pas.
///
/// LE PRODUCTEUR EXISTE DÉSORMAIS — ET SON JUMEAU RESTAURATION AUSSI.
///
/// Ce gestionnaire lit `ORDER-…`, que personne ne posait à l'origine : dans le
/// monolithe c'était Shipping, sous `SHIP-…`. La création est branchée depuis,
/// dans `CreateDeliveryOnOrderConfirmedHandler`.
///
/// ET LE `return` CI-DESSOUS A COÛTÉ TOUT LE PARCOURS RESTAURATION.
///
/// Sortir sur une référence qui n'est pas la nôtre est correct — un
/// `FoodOrderId` lu comme un `OrderId` enverrait `MarkOrderDelivered` sur un
/// GUID absent de cette base, en silence. Mais `FOOD-` a été créé dans le même
/// geste que `ORDER-`, et PERSONNE ne l'a relu : les courses de repas partaient
/// et ne revenaient jamais. Le repas était remis au client, le ticket restait
/// « prêt » à vie, la commande « confirmée », et le restaurateur n'était jamais
/// payé.
///
/// Le symétrique vit maintenant chez son propriétaire —
/// `MarkFoodOrderDeliveredOnDeliveryCompletedHandler`, dans food-service — et
/// revient ici par `FoodOrderDelivered`, qui porte l'`OrderId`. Voir
/// `MarkOrderDeliveredOnFoodOrderDeliveredHandler`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MarkOrderDeliveredOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkOrderDeliveredOnDeliveryCompletedHandler> _logger;

    public MarkOrderDeliveredOnDeliveryCompletedHandler(
        ISender sender, ILogger<MarkOrderDeliveredOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (OrderDeliveryReference.Read(integrationEvent.Reference) is not { } orderId)
        {
            // Course restauration, expédition, ou partenaire externe. Rien à
            // faire, et surtout rien à journaliser : ce chemin est emprunté par
            // la majorité des événements.
            return;
        }

        var result = await _sender.Send(new MarkOrderDeliveredCommand(orderId), cancellationToken);

        // CE HANDLER JOURNALISAIT TOUT, SANS JAMAIS LEVER.
        //
        // L'argument d'origine était juste sur un point — rejouer indéfiniment
        // un état incompatible (commande déjà livrée, jamais confirmée) ne mène
        // nulle part — mais il traitait de la même façon une base indisponible,
        // qui elle aurait abouti au deuxième essai. Un règlement de vendeur
        // était abandonné pour une panne de trente secondes.
        //
        // `SagaOutcome` fait le tri, et porte l'argumentaire complet.
        SagaOutcome.Exiger(
            result, _logger,
            "marquer la commande livrée — SANS ELLE, L'ESCROW RESTE BLOQUÉ ET LE VENDEUR N'EST PAS RÉGLÉ",
            orderId, integrationEvent.DeliveryId);
    }
}
