using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.FoodOrders.Application.Orders.EventHandlers;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LA COURSE A ÉTÉ ANNULÉE → LA COMMANDE DE REPAS PASSE EN ARBITRAGE.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.HoldMealOrderOnDeliveryCancelledHandler")]
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
        if (DeliveryReference.ReadFoodOrder(e.Reference) is not { } ticketId)
        {
            return;
        }

        var ticket = await _food.GetOrderAsync(ticketId, cancellationToken);

        if (ticket is null)
        {
            // ON LÈVE : UNE RÉFÉRENCE `FOOD-` DÉSIGNE FORCÉMENT UN TICKET DE CETTE
            // PLATEFORME.
            _logger.LogError(
                "Ticket de cuisine {FoodOrderId} introuvable après l'annulation de la course "
                + "{DeliveryId}. La commande de repas ne peut pas être mise en arbitrage.",
                ticketId, e.DeliveryId);

            throw new InvalidOperationException(
                $"Ticket de cuisine {ticketId} introuvable : arbitrage impossible.");
        }

        // Le ticket vient de l'autre univers : c'est
        // `HoldOrderOnDeliveryCancelledHandler`, chez order-service, qui s'en
        // charge.
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
