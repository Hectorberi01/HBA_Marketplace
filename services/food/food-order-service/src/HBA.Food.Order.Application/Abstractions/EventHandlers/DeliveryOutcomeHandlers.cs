using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.FoodOrders.Application.Orders.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA COURSE A ÉTÉ ANNULÉE → LA COMMANDE DE REPAS PASSE EN ARBITRAGE.
///
/// ÉCRIT PARCE QUE `UnderReview` ÉTAIT STRUCTURELLEMENT INATTEIGNABLE
/// (ISSUE-061).
///
/// `PutMealOrderUnderReviewCommand` existait, son gestionnaire existait,
/// `MealOrder.MarkUnderReview` existait avec ses quatre gardes, la colonne
/// `ReviewReason` et l'index partiel `ix_meal_orders_under_review` existaient — et
/// AUCUN code n'envoyait jamais cette commande. Zéro route, zéro
/// `ISender.Send`.
///
/// Conséquence en cascade : les DEUX routes d'administration qui SORTENT de
/// l'arbitrage (`/review/resume`, `/review/refund`) exigent `Status ==
/// UnderReview`. Elles répondaient donc 409 `food_ordering.not_under_review` à
/// tous les coups, depuis toujours. Un chemin complet, gardé, migré, indexé — et
/// mort faute d'une porte d'entrée.
///
/// POURQUOI ICI, ET PAS CHEZ order-service QUI LE FAISAIT DÉJÀ.
///
/// `HoldOrderOnDeliveryCancelledHandler` relit bien les références `FOOD-` et
/// envoie `PutOrderUnderReviewCommand`. Mais le préfixe `FOOD-` encode
/// l'identifiant du TICKET DE CUISINE, dont l'`OrderId` vient de deux univers :
/// pour un ticket né d'une `MealOrder`, il envoyait un identifiant que
/// order-service ne connaît pas. Chaque univers arbitre désormais ses propres
/// commandes ; celui-là ignore les tickets marqués « Food ».
///
/// ON N'ANNULE PAS ET ON NE REMBOURSE PAS.
///
/// Même règle que côté marketplace, et elle compte davantage encore ici : une
/// course annulée est le plus souvent RÉATTRIBUABLE — livreur en panne, refus
/// après acceptation, erreur de dispatch. Le sac est prêt, il reste sur le passe,
/// une nouvelle course peut venir le chercher. Rembourser d'office détruirait des
/// ventes récupérables, et l'argent rendu ne se reprend pas.
///
/// CE QUE CE GESTIONNAIRE NE COUVRE PAS.
///
/// Il OUVRE le dossier ; il ne relance aucune course. Tant que personne ne
/// tranche, le repas refroidit. La file d'arbitrage se lit par les deux routes
/// d'administration — qui n'étaient elles-mêmes pas relayées par la passerelle
/// avant ce lot : aucune route `/api/admin/*` n'y existait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class HoldMealOrderOnDeliveryCancelledHandler
    : IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IFoodModuleApi _food;
    private readonly ILogger<HoldMealOrderOnDeliveryCancelledHandler> _logger;

    public HoldMealOrderOnDeliveryCancelledHandler(
        ISender sender,
        IFoodModuleApi food,
        ILogger<HoldMealOrderOnDeliveryCancelledHandler> logger)
    {
        _sender = sender;
        _food = food;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // LA MAJORITÉ DES MESSAGES SORT ICI, ET C'EST NORMAL.
        //
        // Toutes les courses de la plateforme passent sur ce sujet : colis
        // marchands (`ORDER-`), expéditions du monolithe (`SHIP-`), courses de
        // partenaires externes dont la référence ne suit aucune convention. Rien à
        // journaliser sur ce chemin — une ligne par course annulée de la
        // plateforme noierait celles qui comptent.
        if (DeliveryReference.ReadFoodOrder(e.Reference) is not { } ticketId)
        {
            return;
        }

        var ticket = await _food.GetOrderAsync(ticketId, cancellationToken);

        if (ticket is null)
        {
            // ON LÈVE : UNE RÉFÉRENCE `FOOD-` DÉSIGNE FORCÉMENT UN TICKET DE
            // CETTE PLATEFORME.
            //
            // L'introuvable ne peut venir que d'une indisponibilité de
            // restaurant-service — cause passagère, qui aboutira au prochain
            // essai. Sortir en silence laisserait une commande de repas PAYÉE
            // bloquée en « confirmée » pour toujours : ni livraison, ni
            // annulation, ni remboursement, escrow gelé, et un client qui attend
            // un repas que personne n'apportera.
            _logger.LogError(
                "Ticket de cuisine {FoodOrderId} introuvable après l'annulation de la course "
                + "{DeliveryId}. La commande de repas ne peut pas être mise en arbitrage.",
                ticketId, e.DeliveryId);

            throw new InvalidOperationException(
                $"Ticket de cuisine {ticketId} introuvable : arbitrage impossible.");
        }

        // Le ticket vient de l'autre univers : c'est `HoldOrderOnDeliveryCancelledHandler`,
        // chez order-service, qui s'en charge.
        if (!string.Equals(ticket.Origin, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var motif = string.IsNullOrWhiteSpace(e.Reason)
            ? "La course a été annulée."
            : $"La course a été annulée : {e.Reason}";

        var resultat = await _sender.Send(
            new PutMealOrderUnderReviewCommand(ticket.OrderId, motif), cancellationToken);

        if (resultat.IsSuccess)
        {
            _logger.LogWarning(
                "Commande de repas {OrderId} mise en ARBITRAGE : course {DeliveryId} annulée "
                + "({Motif}). Le repas est prêt et attend une décision.",
                ticket.OrderId, e.DeliveryId, e.Reason);

            return;
        }

        // TROIS REFUS QUI NE SONT PAS DES ÉCHECS.
        //
        // L'outbox livre au moins une fois, et une commande peut voir deux courses
        // annulées coup sur coup. Les traiter en erreur ferait sortir une alerte
        // pour un dossier correctement ouvert — ou déjà refermé.
        if (resultat.Error.Code == "food_ordering.already_under_review")
        {
            _logger.LogDebug(
                "Commande de repas {OrderId} déjà en arbitrage — course {DeliveryId} annulée, "
                + "rien à faire.",
                ticket.OrderId, e.DeliveryId);

            return;
        }

        if (resultat.Error.Code is "food_ordering.already_terminal" or "food_ordering.already_delivered")
        {
            _logger.LogInformation(
                "Course {DeliveryId} annulée sur une commande de repas {OrderId} déjà close "
                + "({Code}). Aucun arbitrage ouvert.",
                e.DeliveryId, ticket.OrderId, resultat.Error.Code);

            return;
        }

        // `not_confirmed` N'EST PAS AVALÉ, LUI.
        //
        // Une course existe donc pour une commande qui n'a jamais été confirmée :
        // c'est une incohérence entre les deux services, pas un rejeu. Elle doit
        // se voir.
        _logger.LogCritical(
            "Commande de repas {OrderId} NON mise en arbitrage après l'annulation de la course "
            + "{DeliveryId} — {Code} : {Message}. SANS ARBITRAGE, LA COMMANDE RESTE CONFIRMÉE "
            + "POUR TOUJOURS, PAYÉE ET JAMAIS LIVRÉE.",
            ticket.OrderId, e.DeliveryId, resultat.Error.Code, resultat.Error.Message);

        throw new InvalidOperationException(
            $"Arbitrage impossible pour la commande de repas {ticket.OrderId} : "
            + $"{resultat.Error.Code} — {resultat.Error.Message}");
    }
}
