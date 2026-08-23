using HBA.Food.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Promotions.Application.Promotions;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.Promotions.Api.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX CONSOMMATEURS DU §10.16 : `marketplace.order.cancelled` ET
/// `food.order.cancelled`.
///
/// Deux classes, une seule commande. Du point de vue de la promotion, l'univers
/// d'où vient l'annulation ne change rien à ce qu'il faut défaire : rendre au
/// client son droit d'usage, et rendre à la campagne le budget engagé.
///
/// ON LÈVE SI LA COMPENSATION ÉCHOUE.
///
/// Contrairement à une notification manquée, un budget non rendu ne se rattrape
/// pas tout seul : plus rien ne redemandera cette libération. La reprise bornée du
/// consommateur donne ses chances, puis journalise — ce qu'on veut d'une
/// enveloppe qui se vide sur des commandes qui n'existent plus.
///
/// ET LE REJEU N'EST PAS UN ÉCHEC.
///
/// Kafka livre au moins une fois. `RevokeForCancelledOrder` ne trouve plus d'usage
/// engagé au second passage et rend 0 : la commande réussit sans rien recréditer.
/// C'est la garde qui compte le plus ici — recréditer à chaque livraison ferait
/// une campagne qui ne s'épuise jamais.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le corps partagé par les deux consommateurs.
    ///
    /// Statique et interne : deux gestionnaires qui doivent faire exactement la
    /// même chose ne doivent pas contenir deux fois le même code — c'est ainsi
    /// qu'on corrige un rejeu d'un côté et pas de l'autre.
    /// </summary>
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

/// <summary>
/// La même chose pour le food.
///
/// ON UTILISE `OrderId`, PAS `FoodOrderId`.
///
/// `FoodOrderCancelledIntegrationEvent` porte les deux. La réservation a été prise
/// au checkout avec l'identifiant de commande PARENT — c'est celui que le service
/// de commande transmet à `CommitCoupon`. Prendre `FoodOrderId` ici ne trouverait
/// aucun usage à libérer, et l'échec serait silencieux : la méthode rendrait
/// « succès, rien à faire ».
/// </summary>
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
