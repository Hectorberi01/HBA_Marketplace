using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>
/// Le restaurant refuse ou annule → la commande est annulée.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CES DEUX GESTIONNAIRES, LE CLIENT RESTE DÉBITÉ.
///
/// Un restaurateur refuse une commande — plat épuisé, cuisine saturée, fermeture
/// imprévue. Le ticket passe « refusé », l'événement part… et la commande reste
/// « confirmée ». L'argent est encaissé, le repas n'existera jamais, et rien
/// dans le système ne relie les deux faits.
///
/// C'était l'une des sept ruptures du parcours Food.
///
/// ON ANNULE, ON NE REMBOURSE PAS ICI.
///
/// Le monolithe enchaînait les deux dans le même fichier — annulation puis
/// `RefundPaymentCommand`. Cette commande appartient à financial-service, et
/// order-service n'a pas à la connaître.
///
/// L'annulation publie `OrderCancelled` ; c'est financial qui rembourse en la
/// consommant. La règle tient : gRPC quand on a besoin d'une réponse, Kafka
/// quand on annonce un fait. Ici on annonce.
///
/// « DÉJÀ TERMINALE » N'EST PAS UN ÉCHEC.
///
/// Kafka livre au moins une fois. Traiter le second passage en erreur ferait
/// rejouer le message trois fois puis abandonner en Critical — une alerte pour
/// une commande correctement traitée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// « CE TICKET EST-IL LE MIEN ? »
///
/// ÉCRIT PARCE QUE LA MOITIÉ DES TICKETS NE L'ÉTAIT PAS.
///
/// Le ticket de cuisine de restaurant-service naît de DEUX ponts : une commande
/// order-service dont une ligne est un plat, ou une `MealOrder` de
/// food-order-service. Ses événements portent un `OrderId` — et jusqu'au lot 6.4,
/// rien ne disait de quel univers.
///
/// Les trois gestionnaires de ce service ont chacun un jumeau dans
/// food-order-service, abonné aux MÊMES événements. Sans filtre, pour chaque
/// ticket, l'un des deux jeux travaillait forcément sur un identifiant étranger :
/// « commande introuvable » traité comme une panne, reprises Kafka, puis Critical
/// — sur un fonctionnement parfaitement normal.
///
/// « ABSENT » VAUT « Marketplace », ET C'EST EXACT.
///
/// Un message écrit avant ce lot ne porte pas le champ ; le contrat le rend alors
/// « Marketplace ». Tous les tickets de cette époque viennent de la marketplace :
/// le défaut les décrit, il ne les devine pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class TicketDeLaMarketplace
{
    public static bool Nous(string? origine)
        => !string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase);
}

public sealed class CancelOrderOnFoodOrderRejectedHandler
    : IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelOrderOnFoodOrderRejectedHandler> _logger;

    public CancelOrderOnFoodOrderRejectedHandler(
        ISender sender, ILogger<CancelOrderOnFoodOrderRejectedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
        => TicketDeLaMarketplace.Nous(e.OrderOrigin)
            ? OrderRefusal.AnnulerAsync(_sender, _logger, e.OrderId, Motif(e), cancellationToken)

            // Le refus d'un repas est traité par `CancelMealOrderOnKitchenRejectionHandler`,
            // chez food-order-service. Rien à faire, rien à journaliser : ce chemin
            // est emprunté par la moitié des messages.
            : Task.CompletedTask;

    private static string Motif(FoodOrderRejectedIntegrationEvent e)
        => string.IsNullOrWhiteSpace(e.Comment)
            ? $"Refusée par le restaurant ({e.Reason})."
            : $"Refusée par le restaurant ({e.Reason}) : {e.Comment}";
}

/// <summary>
/// Le ticket est annulé après acceptation → la commande suit.
/// </summary>
/// <remarks>
/// Distinct du refus : ici le restaurant avait accepté, puis quelque chose l'en
/// a empêché. <c>WasInKitchen</c> dit si la préparation avait commencé — ce qui
/// détermine, côté exploitation, si une compensation est due au restaurateur.
/// Le client, lui, est remboursé dans les deux cas.
/// </remarks>
public sealed class CancelOrderOnFoodOrderCancelledHandler
    : IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelOrderOnFoodOrderCancelledHandler> _logger;

    public CancelOrderOnFoodOrderCancelledHandler(
        ISender sender, ILogger<CancelOrderOnFoodOrderCancelledHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
        => !TicketDeLaMarketplace.Nous(e.OrderOrigin)
        ? Task.CompletedTask
        : OrderRefusal.AnnulerAsync(
            _sender,
            _logger,
            e.OrderId,
            $"Annulée par le restaurant{(e.WasInKitchen ? " après mise en préparation" : string.Empty)}"
            + $"{(string.IsNullOrWhiteSpace(e.Reason) ? "." : $" : {e.Reason}")}",
            cancellationToken);
}

/// <summary>
/// Le geste commun aux deux : refuser la commande côté fournisseur.
/// </summary>
/// <remarks>
/// Deux façons d'écrire la même annulation finiraient par diverger — l'une
/// absorberait le rejeu, l'autre non, et la différence ne se verrait qu'en
/// production, un jour de redémarrage.
/// </remarks>
internal static class OrderRefusal
{
    public static async Task AnnulerAsync(
        ISender sender, ILogger logger, Guid orderId, string motif, CancellationToken cancellationToken)
    {
        var resultat = await sender.Send(
            new RejectOrderByProviderCommand(orderId, motif), cancellationToken);

        if (resultat.IsSuccess)
        {
            logger.LogInformation("Commande {OrderId} annulée : {Motif}", orderId, motif);
            return;
        }

        if (resultat.Error.Code == "ordering.already_terminal")
        {
            // Rejeu du message sur une commande déjà close. Rien à faire, et
            // surtout rien à signaler.
            logger.LogDebug(
                "Commande {OrderId} déjà dans un état terminal — annulation ignorée (rejeu).",
                orderId);

            return;
        }

        logger.LogError(
            "Commande {OrderId} NON annulée alors que le restaurant a refusé — {Code} : {Message}. "
            + "Le client reste débité pour un repas qui n'existera pas.",
            orderId, resultat.Error.Code, resultat.Error.Message);

        throw new InvalidOperationException(
            $"Annulation refusée pour la commande {orderId} : "
            + $"{resultat.Error.Code} — {resultat.Error.Message}");
    }
}
