using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Promotions.Application.Promotions;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.Promotions.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// LES DEUX CONSOMMATEURS DU §10.16 : `marketplace.order.cancelled` ET
/// `food.order.cancelled`.
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Promotions.Api.Integration.ReleaseCouponsOnOrderCancelledHandler")]
public sealed class ReleaseCouponsOnOrderCancelledHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<ReleaseCouponsOnOrderCancelledHandler> _logger;

    public ReleaseCouponsOnOrderCancelledHandler(
        ISender sender, ILogger<ReleaseCouponsOnOrderCancelledHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public Task HandleAsync(
        OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
        => LibererAsync(_sender, _logger, e.OrderId, "marketplace", cancellationToken);

    /// <summary>Le corps partagé par les deux consommateurs.</summary>
    internal static async Task LibererAsync(
        ISender sender, ILogger logger, Guid orderId, string univers, CancellationToken cancellationToken)
    {
        var resultat = await sender.Send(
            new ReleaseCouponsForCancelledOrderCommand(orderId), cancellationToken);

        if (resultat.IsSuccess)
        {
            logger.LogInformation(
                "Coupons libérés pour la commande {OrderId} annulée ({Univers}).", orderId, univers);

            return;
        }

        logger.LogError(
            "Coupons NON libérés pour la commande {OrderId} annulée ({Univers}) — {Code} : {Message}. "
            + "Le budget de la campagne reste engagé sur une commande qui n'existe plus.",
            orderId, univers, resultat.Error.Code, resultat.Error.Message);

        throw new InvalidOperationException(
            $"Libération des coupons de la commande {orderId} impossible : "
            + $"{resultat.Error.Code} — {resultat.Error.Message}");
    }
}

/// <summary>La même chose pour le food.</summary>
[NomDeConsommateur("HBA.Promotions.Api.Integration.ReleaseCouponsOnFoodOrderCancelledHandler")]
public sealed class ReleaseCouponsOnFoodOrderCancelledHandler
    : IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<ReleaseCouponsOnFoodOrderCancelledHandler> _logger;

    public ReleaseCouponsOnFoodOrderCancelledHandler(
        ISender sender, ILogger<ReleaseCouponsOnFoodOrderCancelledHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public Task HandleAsync(
        FoodOrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
        => ReleaseCouponsOnOrderCancelledHandler.LibererAsync(
            _sender, _logger, e.OrderId, "food", cancellationToken);
}
