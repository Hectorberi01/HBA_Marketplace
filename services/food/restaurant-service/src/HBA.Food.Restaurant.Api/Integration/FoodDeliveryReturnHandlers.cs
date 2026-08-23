using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Application.Orders;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;

namespace HBA.Food.Api.Integration;

/// <summary>
/// Le retour de course vers le ticket de cuisine : enlèvement, puis remise.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE FICHIER MANQUAIT, ET AUCUNE COMMANDE DE REPAS NE SE TERMINAIT JAMAIS.
///
/// L'aller était branché — repas prêt → course créée sous `FOOD-…`, voir
/// `CreateDeliveryOnFoodOrderReadyHandler`. Le retour, non : AUCUN service du
/// dépôt ne consommait la fin d'une course `FOOD-`. Le livreur remettait le repas
/// au client, et rien ne bougeait nulle part.
///
/// Concrètement : le ticket restait « prêt » à vie sur l'écran de cuisine, la
/// commande commerciale restait « confirmée », `OrderDelivered` n'était jamais
/// publié — donc ni `ReleaseEscrowOnOrderDeliveredHandler` ni
/// `ReleaseEarningsOnOrderDeliveredHandler` ne se déclenchaient, et le gain du
/// restaurateur, comptabilisé dès la confirmation, restait bloqué en « à venir ».
/// **Le repas était remis au client et le restaurateur n'était jamais payé.**
///
/// LE DÉFAUT EST UNE ASYMÉTRIE, ET LE PROCHAIN PRÉFIXE POSERA LE MÊME PIÈGE.
///
/// `ORDER-` et `FOOD-` ont été créés dans le même geste, dans le même fichier
/// (`DeliveryReference`). Seul `ORDER-` a été branché en retour, chez
/// order-service. `FOOD-` est resté un préfixe qu'on POSE sans jamais le RELIRE
/// — et rien ne le signale : `Read` rendant `null` pour ce qui n'est pas à soi,
/// un préfixe que personne ne lit se comporte exactement comme un préfixe que
/// tout le monde ignore à bon droit. Ni la compilation, ni les tests, ni les
/// journaux ne font la différence.
///
/// La règle qui en découle : tout préfixe ajouté à `DeliveryReference` doit être
/// relu par un gestionnaire, sans quoi les courses qu'il désigne partent et ne
/// reviennent pas.
///
/// LES DEUX GESTIONNAIRES VONT ENSEMBLE, ET DANS CET ORDRE.
///
/// `FoodOrder.MarkDelivered` exige l'état « enlevée » — le §20 interdit
/// d'atteindre l'aval sans passer par l'amont. Brancher la seule remise aurait
/// donc produit un conflit `food.order.not_picked_up` sur chaque commande, et la
/// chaîne serait restée rompue au même endroit, avec un journal en plus.
///
/// IL N'Y A PAS DE TROISIÈME GESTIONNAIRE POUR « COURSE ANNULÉE », ET CE
///    N'EST PAS UN OUBLI.
///
/// Une course `FOOD-` annulée ne doit RIEN faire bouger en cuisine : le sac est
/// prêt, il reste sur le passe, et une nouvelle course peut venir le chercher.
/// Le seul geste disponible ici serait `CancelFoodOrderCommand`, qui publie
/// `FoodOrderCancelled` — que order-service consomme en ANNULANT la commande,
/// donc en remboursant. Le détour « propre » aurait produit exactement le
/// remboursement automatique qu'on refuse, sur une course le plus souvent
/// réattribuable.
///
/// La commande commerciale passe donc en ARBITRAGE, et c'est order-service qui
/// relit `FOOD-` pour cela — voir `HoldOrderOnDeliveryCancelledHandler`, qui
/// traduit le ticket en commande par `IFoodModuleApi.GetOrderAsync`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MarkFoodOrderPickedUpOnDeliveryPickedUpHandler
    : IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkFoodOrderPickedUpOnDeliveryPickedUpHandler> _logger;

    public MarkFoodOrderPickedUpOnDeliveryPickedUpHandler(
        ISender sender, ILogger<MarkFoodOrderPickedUpOnDeliveryPickedUpHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryPickedUpIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (FoodOrderReference.Read(integrationEvent.Reference) is not { } foodOrderId)
        {
            // Commande marketplace, expédition, ou partenaire externe. Rien à
            // faire, et surtout rien à journaliser : ce chemin est emprunté par la
            // majorité des événements de course.
            return;
        }

        var resultat = await _sender.Send(
            new MarkFoodOrderPickedUpCommand(foodOrderId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "marquer le repas enlevé — SANS ELLE, LA REMISE SERA REFUSÉE ET LE RESTAURATEUR "
            + "NE SERA PAS PAYÉ",
            foodOrderId, integrationEvent.DeliveryId);
    }
}

/// <summary>
/// Étape finale du parcours restauration : la course est terminée, le ticket
/// passe « livré » — et c'est ce qui déclenche le règlement du restaurateur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SYMÉTRIQUE EXACT DE `MarkOrderDeliveredOnDeliveryCompletedHandler`.
///
/// Même événement, même canal, même mécanique de relecture — seul le préfixe
/// change. Celui d'order-service ne lit que `ORDER-` et sort pour tout le reste,
/// ce qui est correct de sa part : il ne connaît pas les tickets de cuisine, et
/// un `FoodOrderId` lu comme un `OrderId` enverrait `MarkOrderDelivered` sur un
/// GUID absent de sa base — un échec silencieux, puisqu'une commande introuvable
/// ne lève pas. Ce n'était donc pas à lui de combler le trou : c'est ici.
///
/// LE RATTRAPAGE DE L'ENLÈVEMENT N'EST PAS UNE COMMODITÉ.
///
/// Si le message d'enlèvement a été perdu — trois reprises épuisées, base
/// indisponible au mauvais moment —, le ticket est resté « prêt » et la remise
/// est refusée. Or une course TERMINÉE prouve que le sac a été chargé : le fait
/// est certain, il n'est pas déduit. Sans ce rattrapage, un incident de trente
/// secondes sur un message intermédiaire coûterait définitivement le règlement
/// d'un restaurateur, et le journal seul ne le rendrait pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class MarkFoodOrderDeliveredOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkFoodOrderDeliveredOnDeliveryCompletedHandler> _logger;

    public MarkFoodOrderDeliveredOnDeliveryCompletedHandler(
        ISender sender, ILogger<MarkFoodOrderDeliveredOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (FoodOrderReference.Read(integrationEvent.Reference) is not { } foodOrderId)
        {
            return;
        }

        var resultat = await _sender.Send(
            new MarkFoodOrderDeliveredCommand(foodOrderId), cancellationToken);

        if (resultat.IsFailure && resultat.Error.Code == "food.order.not_picked_up")
        {
            _logger.LogWarning(
                "Course {DeliveryId} terminée alors que le ticket {FoodOrderId} n'a jamais été "
                + "marqué enlevé : l'enlèvement a été perdu. On le pose maintenant — une course "
                + "terminée prouve que le sac a été chargé.",
                integrationEvent.DeliveryId, foodOrderId);

            var enlevement = await _sender.Send(
                new MarkFoodOrderPickedUpCommand(foodOrderId), cancellationToken);

            SagaOutcome.Exiger(
                enlevement, _logger,
                "rattraper l'enlèvement manquant avant de marquer le repas livré",
                foodOrderId, integrationEvent.DeliveryId);

            resultat = await _sender.Send(
                new MarkFoodOrderDeliveredCommand(foodOrderId), cancellationToken);
        }

        // ON N'ÉCRIT PAS `=> _sender.Send(...)` ICI.
        //
        // Un `Result` jeté, c'est un message Kafka acquitté sur une étape qui n'a
        // pas eu lieu : ticket jamais clos, commande jamais livrée, restaurateur
        // jamais réglé, et aucune trace. `SagaOutcome` fait le tri entre l'état
        // qui s'y oppose — inutile de rejouer — et la panne passagère, qui elle
        // aboutira au prochain essai.
        SagaOutcome.Exiger(
            resultat, _logger,
            "marquer le repas livré — SANS ELLE, LA COMMANDE NE SE CLÔT PAS, L'ESCROW RESTE "
            + "BLOQUÉ ET LE RESTAURATEUR N'EST PAS PAYÉ",
            foodOrderId, integrationEvent.DeliveryId);
    }
}
