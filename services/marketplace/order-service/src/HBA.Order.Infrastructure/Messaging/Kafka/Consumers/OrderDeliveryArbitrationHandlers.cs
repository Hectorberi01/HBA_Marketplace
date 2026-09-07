using HBA.Deliveries.Contracts;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Application.Orders.EventHandlers;
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// La convention qui empêche la boucle entre « commande annulée » et « course
/// annulée ».
/// </summary>
public static class OrderDeliveryCancellation
{
    /// <summary>Préfixe posé quand c'est NOUS qui annulons la course.</summary>
    public const string ReasonPrefix = "Commande annulée";

    public static string Motif(string? raison)
        => string.IsNullOrWhiteSpace(raison) ? ReasonPrefix : $"{ReasonPrefix} — {raison}";

    public static bool EstNotreFait(string? raison)
        => raison is not null && raison.StartsWith(ReasonPrefix, StringComparison.Ordinal);
}

/// <summary>La course a été annulée → la commande passe en ARBITRAGE.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Api.Integration.HoldOrderOnDeliveryCancelledHandler")]
public sealed class HoldOrderOnDeliveryCancelledHandler
    : IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IFoodModuleApi _food;
    private readonly ILogger<HoldOrderOnDeliveryCancelledHandler> _logger;

    public HoldOrderOnDeliveryCancelledHandler(
        ISender sender,
        IFoodModuleApi food,
        ILogger<HoldOrderOnDeliveryCancelledHandler> logger)
    {
        _sender = sender;
        _food = food;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // C'EST NOUS QUI VENONS D'ANNULER CETTE COURSE. Remettre la commande en
        // arbitrage serait rouvrir un dossier sur une commande close.
        if (OrderDeliveryCancellation.EstNotreFait(e.Reason))
        {
            return;
        }

        var orderId = await ResoudreCommandeAsync(e, cancellationToken);

        if (orderId is not { } commande)
        {
            // Expédition du monolithe (`SHIP-`) ou course d'un partenaire externe.
            return;
        }

        var motif = string.IsNullOrWhiteSpace(e.Reason)
            ? "La course a été annulée."
            : $"La course a été annulée : {e.Reason}";

        var resultat = await _sender.Send(
            new PutOrderUnderReviewCommand(commande, motif), cancellationToken);

        // « DÉJÀ EN ARBITRAGE » N'EST PAS UN ÉCHEC.
        if (resultat.IsFailure && resultat.Error.Code == "ordering.already_under_review")
        {
            _logger.LogDebug(
                "Commande {OrderId} déjà en arbitrage — course {DeliveryId} annulée, rien à faire.",
                commande, e.DeliveryId);

            return;
        }

        // Même raisonnement pour une commande déjà close : une course annulée après
        // la remise, ou après un remboursement décidé, ne rouvre rien.
        if (resultat.IsFailure
            && resultat.Error.Code is "ordering.already_terminal" or "ordering.already_delivered")
        {
            _logger.LogInformation(
                "Course {DeliveryId} annulée sur une commande {OrderId} déjà close ({Code}). "
                + "Aucun arbitrage ouvert.",
                e.DeliveryId, commande, resultat.Error.Code);

            return;
        }

        SagaOutcome.Exiger(
            resultat, _logger,
            "mettre la commande en arbitrage après l'annulation de sa course — SANS ELLE, LA "
            + "COMMANDE RESTE CONFIRMÉE POUR TOUJOURS, PAYÉE ET JAMAIS LIVRÉE",
            commande, e.DeliveryId, e.Reference);
    }

    /// <summary>
    /// De quelle commande commerciale s'agit-il ? <c> null</c> pour tout ce qui
    /// n'est pas à nous — le cas le plus fréquent.
    /// </summary>
    private async Task<Guid?> ResoudreCommandeAsync(
        DeliveryCancelledIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (OrderDeliveryReference.Read(e.Reference) is { } direct)
        {
            return direct;
        }

        if (DeliveryReference.ReadFoodOrder(e.Reference) is not { } ticket)
        {
            return null;
        }

        var repas = await _food.GetOrderAsync(ticket, cancellationToken);

        // CE TICKET N'EST PEUT-ÊTRE PAS LE NÔTRE, ET ON NE POUVAIT PAS LE SAVOIR.
        if (repas is not null
            && string.Equals(repas.Origin, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (repas is null)
        {
            // ON LÈVE : UNE RÉFÉRENCE `FOOD-` DÉSIGNE FORCÉMENT UN TICKET DE CETTE
            // PLATEFORME.
            _logger.LogError(
                "Ticket de cuisine {FoodOrderId} introuvable après l'annulation de la course "
                + "{DeliveryId}. La commande de repas ne peut pas être mise en arbitrage.",
                ticket, e.DeliveryId);

            throw new InvalidOperationException(
                $"Ticket de cuisine {ticket} introuvable : arbitrage impossible.");
        }

        return repas.OrderId;
    }
}

/// <summary>La commande a été annulée → sa course l'est aussi.</summary>
[NomDeConsommateur("HBA.Orders.Api.Integration.CancelDeliveryOnOrderCancelledHandler")]
public sealed class CancelDeliveryOnOrderCancelledHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly IDeliveryDispatchApi _dispatch;
    private readonly ILogger<CancelDeliveryOnOrderCancelledHandler> _logger;

    public CancelDeliveryOnOrderCancelledHandler(
        IDeliveryDispatchApi dispatch, ILogger<CancelDeliveryOnOrderCancelledHandler> logger)
    {
        _dispatch = dispatch;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var resultat = await _dispatch.CancelByReferenceAsync(
            OrderDeliveryReference.For(e.OrderId),

            // La même source qu'à la création — voir
            // `CreateDeliveryOnOrderConfirmedHandler`.
            source: "HbaExpress",
            OrderDeliveryCancellation.Motif(e.Reason),
            cancellationToken);

        if (!resultat.Found)
        {
            // Aucune course : le cas normal.
            return;
        }

        if (!resultat.Cancelled)
        {
            // COLIS DÉJÀ COLLECTÉ, OU COURSE DÉJÀ TERMINÉE.
            _logger.LogError(
                "Commande {OrderId} annulée mais sa course {Reference} n'a PAS pu l'être "
                + "({Motif}). Un colis circule pour une commande annulée : retour à organiser.",
                e.OrderId, OrderDeliveryReference.For(e.OrderId), resultat.Reason);

            return;
        }

        _logger.LogInformation(
            "Course de la commande {OrderId} annulée avec elle : aucun livreur n'ira chercher "
            + "un colis qui ne partira pas.",
            e.OrderId);
    }
}
