using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.FoodOrders.Application.Orders.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LA CUISINE DÉCIDE, ET CE QUE LA COMMANDE EN FAIT.
///
/// TROIS GESTES, ET LE PREMIER EST CELUI QUI MANQUAIT LE PLUS.
///
/// Sans le refus, le ticket passait « refusé » et la commande restait
/// « confirmée » : le client était débité pour un repas qui n'existerait jamais,
/// et rien ne reliait les deux faits.
///
/// Sans la livraison, une commande de repas ne se terminait JAMAIS : elle restait
/// confirmée, l'escrow n'était pas levé, et le gain du restaurateur restait
/// bloqué en « à venir ». Le repas était remis au client, et le restaurateur
/// n'était jamais payé.
///
/// CES GESTES OUVRENT UN DROIT, ILS NE RENDENT PAS L'ARGENT.
///
/// L'annulation publie un fait ; financial-service rembourse en le consommant.
/// Ce service annonce, il n'ordonne pas un virement.
///
/// ET LES TROIS FILTRENT SUR L'ORIGINE DU TICKET.
///
/// Le ticket de cuisine naît de deux ponts : une commande order-service dont une
/// ligne est un plat, ou une `MealOrder` d'ici. Ses événements portent un
/// `OrderId` qui vient donc de deux univers, et order-service a trois
/// gestionnaires jumeaux abonnés aux MÊMES messages.
///
/// Sans filtre, pour chaque ticket, l'un des deux jeux cherchait un identifiant
/// étranger dans sa propre base : introuvable, `SagaOutcome` en faisait une
/// alerte Critical, et les reprises Kafka s'épuisaient — sur un fonctionnement
/// parfaitement normal. C'est exactement la raison d'être de ce champ, du même
/// ordre que le filtre `OrderType` des gestionnaires de paiement, deux fichiers
/// plus loin.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class TicketDeRepas
{
    /// <summary>
    /// Ce ticket vient-il d'une `MealOrder` ?
    ///
    /// EXIGE « Food » EXPLICITEMENT — l'inverse du filtre d'order-service, qui
    /// accepte tout ce qui n'est pas « Food ». Un message d'avant le lot 6.4 ne
    /// porte pas le champ et vaut « Marketplace » : il n'est pas pour nous, et
    /// c'est exact — aucune commande de repas n'avait pu être confirmée avant que
    /// le lot 6.1 n'ouvre son chemin de paiement.
    /// </summary>
    public static bool Nous(string? origine)
        => string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase);
}
public sealed class CancelMealOrderOnKitchenRejectionHandler
    : IIntegrationEventHandler<FoodOrderRejectedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelMealOrderOnKitchenRejectionHandler> _logger;

    public CancelMealOrderOnKitchenRejectionHandler(
        ISender sender, ILogger<CancelMealOrderOnKitchenRejectionHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderRejectedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // Le refus d'un ticket de la marketplace est traité par
        // `CancelOrderOnFoodOrderRejectedHandler`, chez order-service.
        if (!TicketDeRepas.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var motif = string.IsNullOrWhiteSpace(integrationEvent.Comment)
            ? integrationEvent.Reason
            : $"{integrationEvent.Reason} — {integrationEvent.Comment}";

        var resultat = await _sender.Send(
            new RejectMealOrderByRestaurantCommand(integrationEvent.OrderId, motif), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande après refus du restaurant — LE CLIENT A ÉTÉ DÉBITÉ",
            integrationEvent.OrderId, integrationEvent.FoodOrderId);
    }
}

/// <summary>
/// Le ticket a été annulé côté cuisine.
/// </summary>
/// <remarks>
/// MÊME EFFET QUE LE REFUS, ET CE N'EST PAS UN DOUBLON.
///
/// Le REFUS est une décision prise à la réception — plus de riz, four en panne.
/// L'ANNULATION intervient plus tard, parfois alors que la préparation a commencé
/// (`WasInKitchen`). Les deux amènent la commande au même endroit, mais le motif
/// qui atterrit dans le dossier n'est pas le même, et c'est ce que lit
/// l'exploitation.
/// </remarks>
public sealed class CancelMealOrderOnKitchenCancellationHandler
    : IIntegrationEventHandler<FoodOrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelMealOrderOnKitchenCancellationHandler> _logger;

    public CancelMealOrderOnKitchenCancellationHandler(
        ISender sender, ILogger<CancelMealOrderOnKitchenCancellationHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!TicketDeRepas.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var motif = string.IsNullOrWhiteSpace(integrationEvent.Reason)
            ? "Annulée par le restaurant."
            : integrationEvent.Reason;

        var resultat = await _sender.Send(
            new RejectMealOrderByRestaurantCommand(integrationEvent.OrderId, motif), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande après annulation du ticket de cuisine",
            integrationEvent.OrderId, integrationEvent.FoodOrderId);
    }
}

/// <summary>
/// Le repas a été remis au client : la commande se clôt.
/// </summary>
/// <remarks>
/// C'EST LA CUISINE QUI TRADUIT, PAS LA LIVRAISON.
///
/// La fin de course porte une référence « FOOD-… » dont le GUID est celui du
/// TICKET, inconnu de cette base. C'est restaurant-service qui fait la
/// correspondance, en publiant cet événement avec l'`OrderId`.
/// </remarks>
public sealed class MarkMealOrderDeliveredOnKitchenDeliveryHandler
    : IIntegrationEventHandler<FoodOrderDeliveredIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<MarkMealOrderDeliveredOnKitchenDeliveryHandler> _logger;

    public MarkMealOrderDeliveredOnKitchenDeliveryHandler(
        ISender sender, ILogger<MarkMealOrderDeliveredOnKitchenDeliveryHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!TicketDeRepas.Nous(integrationEvent.OrderOrigin))
        {
            return;
        }

        var resultat = await _sender.Send(
            new MarkMealOrderDeliveredCommand(integrationEvent.OrderId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "clore la commande après remise du repas — L'ESCROW RESTE GELÉ ET LE RESTAURATEUR N'EST PAS RÉGLÉ",
            integrationEvent.OrderId, integrationEvent.FoodOrderId);
    }
}
