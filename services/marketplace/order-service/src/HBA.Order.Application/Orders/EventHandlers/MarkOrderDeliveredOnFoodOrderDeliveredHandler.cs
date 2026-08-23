using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>
/// Le repas est remis au client → la commande commerciale passe « livrée ».
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DERNIER MAILLON AVANT L'ARGENT, ET IL N'EXISTAIT PAS.
///
/// Une commande de repas livrée ne se terminait jamais. Elle restait
/// « confirmée » indéfiniment : `OrderDelivered` n'était donc jamais publié, ni
/// `ReleaseEscrowOnOrderDeliveredHandler` ni `ReleaseEarningsOnOrderDeliveredHandler`
/// ne se déclenchaient, et le gain du restaurateur — comptabilisé dès la
/// confirmation par `AccrueEarningsOnOrderConfirmedHandler`, en « à venir » —
/// n'était jamais libéré. **Le repas était remis au client et le restaurateur
/// n'était jamais payé.**
///
/// POURQUOI CE N'EST PAS `MarkOrderDeliveredOnDeliveryCompletedHandler` QUI
///    AURAIT DÛ S'EN CHARGER.
///
/// Ce gestionnaire-là lit la référence de course, et ne retient que `ORDER-`. Une
/// course de repas porte `FOOD-…`, dont le GUID est celui du TICKET DE CUISINE,
/// pas de la commande. Le lire comme un `OrderId` enverrait `MarkOrderDelivered`
/// sur un identifiant absent de cette base — un échec silencieux, puisqu'une
/// commande introuvable ne lève pas. Et order-service n'a aucun moyen de traduire
/// l'un en l'autre : seul Food connaît la correspondance.
///
/// D'où le chemin en deux temps : food-service relit `FOOD-`, clôt son ticket, et
/// publie `FoodOrderDelivered` — qui porte l'`OrderId`, comme toute cette
/// famille. C'est ce que ce gestionnaire consomme.
///
/// LE DÉFAUT EST UNE ASYMÉTRIE, ET LE PROCHAIN PRÉFIXE POSERA LE MÊME PIÈGE.
///
/// `ORDER-` et `FOOD-` ont été créés au même moment, dans `DeliveryReference`.
/// Seul `ORDER-` a été branché en retour. Rien ne signale un préfixe qu'on pose
/// sans jamais le relire : `Read` rend `null` pour ce qui n'est pas à soi, si
/// bien qu'un préfixe orphelin se comporte exactement comme un préfixe
/// légitimement ignoré. Ni la compilation, ni les tests ne les distinguent.
///
/// MÊME PLACE DANS LA CHAÎNE QUE LE JUMEAU MARCHANDISE, DONC MÊME EXIGENCE
///    SUR LE `Result`.
///
/// Un `Result` jeté ici acquitte un message sur une étape qui n'a pas eu lieu :
/// commande jamais close, escrow jamais levé, aucune trace. `SagaOutcome` tranche
/// entre l'état qui s'y oppose — rejouer ne mènerait nulle part — et la panne
/// passagère, qui aboutira au deuxième essai.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MarkOrderDeliveredOnFoodOrderDeliveredHandler
    : IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkOrderDeliveredOnFoodOrderDeliveredHandler> _logger;

    public MarkOrderDeliveredOnFoodOrderDeliveredHandler(
        ISender sender, ILogger<MarkOrderDeliveredOnFoodOrderDeliveredHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LA MOITIÉ DE CES MESSAGES NE NOUS CONCERNE PAS.
        //
        // Le ticket de cuisine naît de deux ponts, et son `OrderId` vient de deux
        // univers. La remise d'un repas du parcours food est close par
        // `MarkMealOrderDeliveredOnKitchenDeliveryHandler`, chez
        // food-order-service. Sans ce filtre, on cherchait ici un identifiant de
        // `MealOrder` — introuvable — et `SagaOutcome` en faisait une alerte
        // Critical sur un fonctionnement normal.
        if (!TicketDeLaMarketplace.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var result = await _sender.Send(
            new MarkOrderDeliveredCommand(integrationEvent.OrderId), cancellationToken);

        SagaOutcome.Exiger(
            result, _logger,
            "marquer la commande de repas livrée — SANS ELLE, L'ESCROW RESTE BLOQUÉ ET LE "
            + "RESTAURATEUR N'EST PAS RÉGLÉ",
            integrationEvent.OrderId, integrationEvent.FoodOrderId, integrationEvent.RestaurantId);
    }
}
