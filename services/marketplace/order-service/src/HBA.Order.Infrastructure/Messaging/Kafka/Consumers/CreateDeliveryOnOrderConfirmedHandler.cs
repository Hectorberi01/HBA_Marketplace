using HBA.Deliveries.Contracts;
using HBA.Inventory.Contracts;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Application.Orders.EventHandlers;
using HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Orders.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Commande marketplace payée → une course est demandée.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Api.Integration.CreateDeliveryOnOrderConfirmedHandler")]
public sealed class CreateDeliveryOnOrderConfirmedHandler
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly IDeliveryDispatchApi _dispatch;
    private readonly IOrderingModuleApi _ordering;
    private readonly IInventoryModuleApi _inventory;
    private readonly ISender _sender;
    private readonly ILogger<CreateDeliveryOnOrderConfirmedHandler> _logger;

    public CreateDeliveryOnOrderConfirmedHandler(
        IDeliveryDispatchApi dispatch,
        IOrderingModuleApi ordering,
        IInventoryModuleApi inventory,
        ISender sender,
        ILogger<CreateDeliveryOnOrderConfirmedHandler> logger)
    {
        _dispatch = dispatch;
        _ordering = ordering;
        _inventory = inventory;
        _sender = sender;
        _logger = logger;
    }

    public Task HandleAsync(
        OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
        => string.Equals(e.Kind, "Food", StringComparison.Ordinal)
            ? Task.CompletedTask
            : DemanderCourseAsync(e.OrderId, cancellationToken);

    /// <summary>Demande la course d'une commande de MARCHANDISE confirmée.</summary>
    public async Task DemanderCourseAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var commande = await _ordering.GetOrderAsync(orderId, cancellationToken);

        if (commande is null)
        {
            _logger.LogError(
                "Commande {OrderId} introuvable à la confirmation. Aucune course demandée : "
                + "elle n'atteindra jamais « livrée » et son vendeur ne sera pas réglé.",
                orderId);

            throw new InvalidOperationException($"Commande {orderId} introuvable.");
        }

        var lieux = commande.Lines
            .Select(l => l.ShipFromLocationId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (lieux.Count == 0)
        {
            _logger.LogError(
                "Commande {OrderId} sans lieu d'expédition sur ses lignes. Aucune course possible.",
                orderId);

            throw new InvalidOperationException($"Commande {orderId} : aucun lieu d'expédition.");
        }

        if (lieux.Count > 1)
        {
            // ON REFUSE DE CRÉER LA COURSE — ET CE REFUS A UNE SORTIE.
            _logger.LogError(
                "Commande {OrderId} expédiée depuis {Nombre} lieux : le multi-colis n'est pas "
                + "supporté. AUCUNE course créée — la commande passe en ARBITRAGE.",
                orderId, lieux.Count);

            var arbitrage = await _sender.Send(
                new PutOrderUnderReviewCommand(
                    orderId,
                    $"Expédition depuis {lieux.Count} lieux : le multi-colis n'est pas encore "
                    + "supporté. Regrouper les articles sur un seul lieu puis relancer, ou "
                    + "rembourser."),
                cancellationToken);

            SagaOutcome.Exiger(
                arbitrage, _logger,
                "mettre la commande en arbitrage faute de course possible — SANS ELLE, LA "
                + "COMMANDE RESTE CONFIRMÉE POUR TOUJOURS, PAYÉE ET JAMAIS LIVRÉE",
                orderId, lieux.Count);

            return;
        }

        var lieu = await _inventory.GetLocationAsync(lieux[0], cancellationToken);
        var adresse = commande.ShippingAddress;

        var manquants = new List<string>();

        if (lieu is null)
        {
            manquants.Add("lieu d'expédition introuvable");
        }

        if (adresse is null)
        {
            manquants.Add("adresse de livraison du client");
        }

        if (manquants.Count > 0)
        {
            _logger.LogError(
                "Course NON créée pour la commande {OrderId}. Données manquantes : {Manquants}. "
                + "La commande n'atteindra pas « livrée » et son vendeur ne sera pas réglé.",
                orderId, string.Join(", ", manquants));

            throw new InvalidOperationException(
                $"Course impossible pour la commande {orderId} : {string.Join(", ", manquants)}");
        }

        var demande = new CreateDeliveryRequest(
            Reference: OrderDeliveryReference.For(orderId),

            // « HbaExpress » : c'est de la marchandise, pas un repas.
            Source: "HbaExpress",

            // « Standard » ET NON « Express », CONTRAIREMENT AUX REPAS.
            Type: "Standard",
            Pickup: new DeliveryStopRequest(
                ContactName: null,
                Phone: lieu!.ContactPhone,
                Commune: lieu.CommuneName,
                Quartier: lieu.Quartier,
                Landmark: lieu.Landmark,
                Instructions: lieu.Line,
                Latitude: lieu.Latitude,
                Longitude: lieu.Longitude),
            Dropoff: new DeliveryStopRequest(
                ContactName: adresse!.Recipient,
                Phone: adresse.Phone,
                Commune: adresse.CommuneName,
                Quartier: adresse.Quartier,
                Landmark: adresse.Landmark,
                Instructions: adresse.Line1,
                Latitude: adresse.Latitude,
                Longitude: adresse.Longitude),
            Package: new DeliveryPackageRequest(
                Description: $"Commande {commande.Id:N} — {commande.Lines.Count} article(s)",
                WeightKg: null,
                IsFragile: false,
                IsPerishable: false),

            // CE QUE VALENT LES MARCHANDISES — POUR QUE LA COURSE EXIGE UNE PREUVE
            // (ISSUE-057).
            DeclaredValue: commande.Subtotal,

            // TOUJOURS `false` ICI, ET CE N'EST PAS UN OUBLI.
            IsCashOnDelivery: false,

            // LE DEVIS FIGÉ AU CHECKOUT : C'EST CE QUE LE CLIENT A PAYÉ.
            QuoteId: commande.DeliveryQuoteId);

        var course = await _dispatch.CreateAsync(demande, cancellationToken);

        if (course.Succeeded)
        {
            _logger.LogInformation(
                "Course {DeliveryId} créée pour la commande {OrderId}.", course.DeliveryId, orderId);

            return;
        }

        // C'EST LE SEUL ENDROIT OÙ LE PRIX PAYÉ ET LE PRIX ACHETÉ PEUVENT ENCORE
        // DIVERGER, ET IL EST DÉLIBÉRÉ.
        var devisEnCause = course.ReasonCode is "pricing.quote_not_usable" or "pricing.quote.malformed";

        if (devisEnCause && !string.IsNullOrWhiteSpace(commande.DeliveryQuoteId))
        {
            _logger.LogWarning(
                "Devis payé {QuoteId} refusé pour la commande {OrderId} ({Code} — {Motif}). Nouvelle "
                + "tentative sans devis — le prix acheté peut différer de celui payé.",
                commande.DeliveryQuoteId, orderId, course.ReasonCode, course.Reason);

            var secondEssai = await _dispatch.CreateAsync(
                demande with { QuoteId = null }, cancellationToken);

            if (secondEssai.Succeeded)
            {
                _logger.LogInformation(
                    "Course {DeliveryId} créée pour la commande {OrderId}, hors devis payé.",
                    secondEssai.DeliveryId, orderId);

                return;
            }

            course = secondEssai;
        }

        _logger.LogError(
            "Course NON créée pour la commande {OrderId} — {Code} : {Motif}. Le vendeur ne sera pas réglé.",
            orderId, course.ReasonCode, course.Reason);

        // ON LÈVE PLUTÔT QUE DE METTRE EN ARBITRAGE, ET C'EST DÉLIBÉRÉ.
        throw new InvalidOperationException(
            $"Course refusée pour la commande {orderId} : {course.ReasonCode} — {course.Reason}");
    }
}
