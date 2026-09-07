using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Orders.Application.Orders;
using HBA.Orders.Application.Orders.EventHandlers;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le restaurant refuse ou annule → la commande est annulée.</summary>
/// <summary>« CE TICKET EST-IL LE MIEN ? »</summary>
internal static class TicketDeLaMarketplace
{
    public static bool Nous(string? origine)
        => !string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase);
}

// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.CancelOrderOnFoodOrderRejectedHandler")]
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

            // Le refus d'un repas est traité par
            // `CancelMealOrderOnKitchenRejectionHandler`, chez food-order-service.
            : Task.CompletedTask;

    private static string Motif(FoodOrderRejectedIntegrationEvent e)
        => string.IsNullOrWhiteSpace(e.Comment)
            ? $"Refusée par le restaurant ({e.Reason})."
            : $"Refusée par le restaurant ({e.Reason}) : {e.Comment}";
}

/// <summary>Le ticket est annulé après acceptation → la commande suit.</summary>
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.CancelOrderOnFoodOrderCancelledHandler")]
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

/// <summary>Le geste commun aux deux : refuser la commande côté fournisseur.</summary>
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
            // Rejeu du message sur une commande déjà close.
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
