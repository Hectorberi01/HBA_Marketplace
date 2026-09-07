using HBA.Deliveries.Contracts;
using HBA.Food.Application.Orders;
using HBA.Food.Contracts;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts;
using HBA.Inventory.Contracts;
using HBA.Ordering.Contracts;
using Microsoft.Extensions.Logging;

// LE CONTRAT DE L'ÉVÉNEMENT VIENT D'order-service, SON PUBLIEUR.
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using MediatR;

using HBA.Food.Domain.Orders;

namespace HBA.Food.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>La référence sous laquelle un ticket de cuisine se reconnaît dans une course.</summary>
public static class FoodOrderReference
{
    // DÉLÉGUÉ AU SOCLE PARTAGÉ — voir `DeliveryReference`.
    public static string For(Guid foodOrderId) => DeliveryReference.ForFoodOrder(foodOrderId);

    public static Guid? Read(string? reference) => DeliveryReference.ReadFoodOrder(reference);
}

/// <summary>Commande de repas payée → ticket de cuisine ouvert.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Food.Api.Integration.ReceiveFoodOrderOnOrderConfirmedHandler")]
public sealed class ReceiveFoodOrderOnOrderConfirmedHandler
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IOrderingModuleApi _ordering;
    private readonly ILogger<ReceiveFoodOrderOnOrderConfirmedHandler> _logger;

    public ReceiveFoodOrderOnOrderConfirmedHandler(
        ISender sender,
        IOrderingModuleApi ordering,
        ILogger<ReceiveFoodOrderOnOrderConfirmedHandler> logger)
    {
        _sender = sender;
        _ordering = ordering;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(integrationEvent.Kind, "Food", StringComparison.Ordinal))
        {
            return;
        }

        if (integrationEvent.RestaurantId is not { } restaurantId)
        {
            _logger.LogError(
                "Commande {OrderId} confirmée en restauration SANS restaurant. Le client est "
                + "débité et aucune cuisine ne peut être servie.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande {integrationEvent.OrderId} : restauration sans restaurant.");
        }

        // ON RELIT LA COMMANDE, ON NE L'ÉLARGIT PAS.
        var commande = await _ordering.GetOrderAsync(integrationEvent.OrderId, cancellationToken);

        if (commande is null)
        {
            _logger.LogError(
                "Commande {OrderId} introuvable à la confirmation. Aucun ticket de cuisine ouvert.",
                integrationEvent.OrderId);

            throw new InvalidOperationException($"Commande {integrationEvent.OrderId} introuvable.");
        }

        var lignes = commande.Lines
            .Where(l => string.Equals(l.Kind, "Food", StringComparison.Ordinal))
            .Select(l => new FoodOrderLineInput(
                l.MenuItemId,
                l.Quantity,
                l.Options?.Select(o => o.OptionId).ToList() ?? [],
                l.Notes))
            .ToList();

        if (lignes.Count == 0)
        {
            // CE CAS ÉTAIT INVISIBLE JUSQU'À AUJOURD'HUI.
            _logger.LogError(
                "Commande {OrderId} déclarée « Food » mais sans aucune ligne de repas. "
                + "Aucun ticket ouvert.",
                integrationEvent.OrderId);

            throw new InvalidOperationException(
                $"Commande {integrationEvent.OrderId} : aucune ligne de restauration.");
        }

        var resultat = await _sender.Send(
            new ReceiveFoodOrderCommand(
                // L'identifiant vient d'order-service : le ticket devra donc y
                // retourner pour l'adresse de livraison et pour l'arbitrage.
                FoodOrderOrigin.Marketplace,
                integrationEvent.OrderId,
                restaurantId,
                lignes,
                CustomerNote: null),
            cancellationToken);

        if (resultat.IsFailure)
        {
            _logger.LogError(
                "Ticket de cuisine NON ouvert pour la commande {OrderId} — {Code} : {Message}.",
                integrationEvent.OrderId, resultat.Error.Code, resultat.Error.Message);

            throw new InvalidOperationException(
                $"Ticket impossible pour la commande {integrationEvent.OrderId} : "
                + $"{resultat.Error.Code} — {resultat.Error.Message}");
        }

        _logger.LogInformation(
            "Ticket de cuisine {FoodOrderId} ouvert pour la commande {OrderId} ({Lignes} ligne(s)).",
            resultat.Value, integrationEvent.OrderId, lignes.Count);
    }
}

// PUBLICS tous les deux : ils apparaissent dans la signature du constructeur de
// `CreateDeliveryOnFoodOrderReadyHandler`, qui est public parce que le conteneur
// l'instancie.
/// <summary>Repas prêt → un livreur est cherché.</summary>
/// <summary>
/// Ce que la CRÉATION DE COURSE a besoin de savoir de la commande commerciale, quel
/// que soit l'univers dont elle vient.
/// </summary>
public sealed record CommandeALivrer(
    string? Recipient,
    string? Phone,
    string? CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    double? Latitude,
    double? Longitude,
    decimal Subtotal,
    string? DeliveryQuoteId);

/// <summary>LIRE LA COMMANDE D'UN TICKET, DANS L'UNIVERS QUI LA PORTE.</summary>
public sealed class LecteurDeCommandeALivrer
{
    private readonly IOrderingModuleApi _marketplace;
    private readonly IMealOrderModuleApi _repas;

    public LecteurDeCommandeALivrer(IOrderingModuleApi marketplace, IMealOrderModuleApi repas)
    {
        _marketplace = marketplace;
        _repas = repas;
    }

    public async Task<CommandeALivrer?> LireAsync(
        string origine, Guid orderId, CancellationToken cancellationToken)
    {
        if (string.Equals(origine, FoodOrderOrigins.Food, StringComparison.OrdinalIgnoreCase))
        {
            var repas = await _repas.GetOrderAsync(orderId, cancellationToken);
            if (repas is null)
            {
                return null;
            }

            var adresse = repas.ShippingAddress;

            return new CommandeALivrer(
                adresse?.Recipient, adresse?.Phone, adresse?.CommuneName, adresse?.Quartier,
                adresse?.Landmark, adresse?.Line1, adresse?.Latitude, adresse?.Longitude,
                repas.Subtotal, repas.DeliveryQuoteId);
        }

        // TOUT CE QUI N'EST PAS EXPLICITEMENT « Food » EST TRAITÉ COMME
        // MARKETPLACE, ET C'EST COHÉRENT AVEC LE DÉFAUT DU CONTRAT.
        var commande = await _marketplace.GetOrderAsync(orderId, cancellationToken);
        if (commande is null)
        {
            return null;
        }

        var expedition = commande.ShippingAddress;

        return new CommandeALivrer(
            expedition?.Recipient, expedition?.Phone, expedition?.CommuneName, expedition?.Quartier,
            expedition?.Landmark, expedition?.Line1, expedition?.Latitude, expedition?.Longitude,
            commande.Subtotal, commande.DeliveryQuoteId);
    }
}

[NomDeConsommateur("HBA.Food.Api.Integration.CreateDeliveryOnFoodOrderReadyHandler")]
public sealed class CreateDeliveryOnFoodOrderReadyHandler
    : IIntegrationEventHandler<FoodOrderReadyForPickupIntegrationEvent>
{
    private readonly IDeliveryDispatchApi _dispatch;
    private readonly IFoodModuleApi _food;
    private readonly LecteurDeCommandeALivrer _commandes;
    private readonly IInventoryModuleApi _inventory;
    private readonly ILogger<CreateDeliveryOnFoodOrderReadyHandler> _logger;

    public CreateDeliveryOnFoodOrderReadyHandler(
        IDeliveryDispatchApi dispatch,
        IFoodModuleApi food,
        LecteurDeCommandeALivrer commandes,
        IInventoryModuleApi inventory,
        ILogger<CreateDeliveryOnFoodOrderReadyHandler> logger)
    {
        _dispatch = dispatch;
        _food = food;
        _commandes = commandes;
        _inventory = inventory;
        _logger = logger;
    }

    public async Task HandleAsync(
        FoodOrderReadyForPickupIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var manquants = new List<string>();

        var restaurant = await _food.GetRestaurantAsync(e.RestaurantId, cancellationToken);

        // L'UNIVERS D'ABORD. Cette ligne interrogeait order-service en dur, et un
        // ticket né d'une `MealOrder` n'y existe pas : voir
        // `LecteurDeCommandeALivrer`.
        var commande = await _commandes.LireAsync(e.OrderOrigin, e.OrderId, cancellationToken);

        if (restaurant is null)
        {
            manquants.Add("restaurant introuvable");
        }

        if (commande is null)
        {
            manquants.Add($"commande introuvable dans l'univers « {e.OrderOrigin} »");
        }

        var lieu = restaurant?.FulfillmentLocationId is { } locationId
            ? await _inventory.GetLocationAsync(locationId, cancellationToken)
            : null;

        if (lieu is null)
        {
            manquants.Add("lieu de collecte du restaurant");
        }

        // ON EXIGE LE REPÈRE ET LA POSITION, PAS « une adresse non nulle ».
        if (commande is not null && string.IsNullOrWhiteSpace(commande.Landmark))
        {
            manquants.Add("point de repère de l'adresse de livraison");
        }

        if (commande is not null && (commande.Latitude is null || commande.Longitude is null))
        {
            manquants.Add("position de l'adresse de livraison");
        }

        if (manquants.Count > 0)
        {
            _logger.LogError(
                "Course NON créée pour le ticket {FoodOrderId} (commande {OrderId} de l'univers "
                + "{Origine}, restaurant {RestaurantId}). Données manquantes : {Manquants}. Le repas "
                + "est prêt et refroidit sans qu'aucun livreur ne soit cherché.",
                e.FoodOrderId, e.OrderId, e.OrderOrigin, e.RestaurantId, string.Join(", ", manquants));

            throw new InvalidOperationException(
                $"Course impossible pour le ticket {e.FoodOrderId} : {string.Join(", ", manquants)}");
        }

        var demande = new CreateDeliveryRequest(
            Reference: FoodOrderReference.For(e.FoodOrderId),

            // « HbaFood », PAS « HbaExpress ».
            Source: "HbaFood",
            Type: "Express",
            Pickup: new DeliveryStopRequest(
                ContactName: restaurant!.Name,
                Phone: lieu!.ContactPhone,
                Commune: lieu.CommuneName,
                Quartier: lieu.Quartier,
                Landmark: lieu.Landmark,
                Instructions: null,
                Latitude: lieu.Latitude,
                Longitude: lieu.Longitude),
            Dropoff: new DeliveryStopRequest(
                ContactName: commande!.Recipient,
                Phone: commande.Phone,
                Commune: commande.CommuneName,
                Quartier: commande.Quartier,
                Landmark: commande.Landmark,
                Instructions: commande.Line1,
                Latitude: commande.Latitude,
                Longitude: commande.Longitude),
            Package: new DeliveryPackageRequest(
                Description: $"Repas — {restaurant.Name}",
                WeightKg: null,
                IsFragile: false,
                IsPerishable: true),

            // CE QUE VALENT LES MARCHANDISES — POUR QUE LA COURSE EXIGE UNE PREUVE
            // (ISSUE-057).
            DeclaredValue: commande.Subtotal,

            // TOUJOURS `false` ICI. Le ticket est réglé au moment de la commande —
            // c'est ce que suppose `DeliveryQuoteId`, un devis DÉJÀ PAYÉ.
            IsCashOnDelivery: false,
            QuoteId: commande.DeliveryQuoteId);

        var course = await _dispatch.CreateAsync(demande, cancellationToken);

        if (course.Succeeded)
        {
            _logger.LogInformation(
                "Course {DeliveryId} créée pour le ticket {FoodOrderId}.",
                course.DeliveryId, e.FoodOrderId);

            return;
        }

        // ON N'ABANDONNE PAS SUR UN DEVIS PÉRIMÉ : ON RETENTE SANS LUI.
        if (!string.IsNullOrWhiteSpace(commande.DeliveryQuoteId))
        {
            _logger.LogWarning(
                "Le devis payé {QuoteId} du ticket {FoodOrderId} a été refusé ({Motif}). "
                + "Nouvelle tentative sans devis — le prix acheté peut différer de celui payé.",
                commande.DeliveryQuoteId, e.FoodOrderId, course.Reason);

            var secondEssai = await _dispatch.CreateAsync(
                demande with { QuoteId = null }, cancellationToken);

            if (secondEssai.Succeeded)
            {
                _logger.LogInformation(
                    "Course {DeliveryId} créée pour le ticket {FoodOrderId}, hors devis payé.",
                    secondEssai.DeliveryId, e.FoodOrderId);

                return;
            }

            course = secondEssai;
        }

        _logger.LogError(
            "Course NON créée pour le ticket {FoodOrderId} — {Motif}. Le repas est prêt et "
            + "refroidit.",
            e.FoodOrderId, course.Reason);

        throw new InvalidOperationException(
            $"Course refusée pour le ticket {e.FoodOrderId} : {course.Reason}");
    }
}
