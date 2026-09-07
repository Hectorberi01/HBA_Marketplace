using HBA.Food.Application.Orders;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

using HBA.Food.Domain.Orders;

namespace HBA.Food.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Commande de repas confirmée chez food-order-service → ticket de cuisine ouvert.
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Food.Api.Integration.ReceiveFoodOrderOnMealOrderConfirmedHandler")]
public sealed class ReceiveFoodOrderOnMealOrderConfirmedHandler
    : IIntegrationEventHandler<MealOrderConfirmedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<ReceiveFoodOrderOnMealOrderConfirmedHandler> _logger;

    public ReceiveFoodOrderOnMealOrderConfirmedHandler(
        ISender sender,
        ILogger<ReceiveFoodOrderOnMealOrderConfirmedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        MealOrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // AUCUN FILTRE SUR UN DISCRIMINANT, CONTRAIREMENT À L'ANCIEN CHEMIN.

        // Le restaurant est `required` au contrat, donc jamais absent — mais «
        // présent » et « renseigné » sont deux choses différentes, et un Guid vide
        // passe la contrainte du compilateur.
        if (integrationEvent.RestaurantId == Guid.Empty)
        {
            _logger.LogError(
                "Commande de repas {OrderId} confirmée SANS restaurant. Le client est débité et "
                + "aucune cuisine ne peut être servie.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande de repas {integrationEvent.OrderId} : confirmation sans restaurant.");
        }

        // LES OPTIONS SE RÉDUISENT À LEURS IDENTIFIANTS, ET LES PRIX SE PERDENT ICI
        // VOLONTAIREMENT.
        var lignes = integrationEvent.Lines
            .Select(l => new FoodOrderLineInput(
                l.MenuItemId,
                l.Quantity,
                l.Options.Select(o => o.OptionId).ToList(),
                l.Notes))
            .ToList();

        if (lignes.Count == 0)
        {
            _logger.LogError(
                "Commande de repas {OrderId} confirmée sans aucune ligne. Aucun ticket ouvert.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande de repas {integrationEvent.OrderId} : aucune ligne à préparer.");
        }

        var resultat = await _sender.Send(
            new ReceiveFoodOrderCommand(
                // L'IDENTIFIANT EST CELUI D'UNE `MealOrder`, PAS D'UNE COMMANDE
                // order-service.
                FoodOrderOrigin.Food,
                integrationEvent.OrderId,
                integrationEvent.RestaurantId,
                lignes,

                // LA NOTE DU CLIENT ARRIVE ENFIN JUSQU'À LA CUISINE.
                integrationEvent.CustomerNote),
            cancellationToken);

        if (resultat.IsFailure)
        {
            _logger.LogError(
                "Ticket de cuisine NON ouvert pour la commande de repas {OrderId} — {Code} : {Message}.",
                integrationEvent.OrderId, resultat.Error.Code, resultat.Error.Message);

            throw new InvalidOperationException(
                $"Ticket impossible pour la commande de repas {integrationEvent.OrderId} : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Ticket de cuisine {FoodOrderId} ouvert pour la commande de repas {OrderId} "
            + "({Lignes} ligne(s)).",
            resultat.Value, integrationEvent.OrderId, lignes.Count);
    }
}
