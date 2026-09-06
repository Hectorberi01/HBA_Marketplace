using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.FoodOrders.Application.Orders.Commands;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using MediatR;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
//
// Il vivait dans `HBA.FoodOrders.Application.Orders.EventHandlers` et y resolvait ses voisins SANS `using` : le
// compilateur cherche d'abord dans les espaces de noms englobants. Descendu
// dans `Messaging/Kafka/Consumers`, il a perdu ce voisinage — d'ou les lignes
// ci-dessous, qui rendent explicite ce qui etait implicite.
using HBA.FoodOrders.Application.Orders.EventHandlers;

namespace HBA.FoodOrders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX GESTIONNAIRES QUI TIENNENT L'ARGENT.
///
/// ILS INSPECTENT LE RÉSULTAT, PARCE QUE LEURS ANCÊTRES NE LE FAISAIENT PAS.
///
/// Ils s'écrivaient en une flèche :
///
///     => _sender.Send(new ConfirmOrderPaymentCommand(e.OrderId), ct);
///
/// La commande pouvait refuser — introuvable, déjà annulée — et le message Kafka
/// était acquitté quand même. Paiement encaissé, commande jamais confirmée,
/// silence complet. C'était invisible sur une carte des événements : le câblage
/// était correct, seul l'EFFET manquait.
///
/// La règle de tri — journaliser un échec d'état, lever sur une cause passagère —
/// vit dans <see cref="SagaOutcome"/>, avec son argumentaire.
///
/// ET ILS FILTRENT SUR `OrderType`.
///
/// Le même sujet Kafka porte les paiements des deux univers. Sans ce filtre, ce
/// service chercherait chez lui l'identifiant d'une commande marketplace, ne le
/// trouverait pas, et ne pourrait pas distinguer « pas pour moi » de « ma
/// commande a disparu » — c'est-à-dire une alerte de tous les instants sur un
/// fonctionnement normal. C'est précisément la raison d'être de ce champ, que le
/// contrat de paiement documente déjà.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
//
// `IntegrationEventDispatcher` la derivait du nom complet du type. Descendre ce
// fichier dans `Messaging/Kafka/Consumers` a change son espace de noms, donc sa
// cle, donc a orpheline ses traces dans `consumer_inbox` : au premier rejeu,
// chaque evenement deja traite serait repasse pour neuf.
//
// Les valeurs ci-dessous reproduisent le nom complet d'AVANT le deplacement.
// Ce sont des cles de base de donnees : elles ne se refactorisent pas.
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.ConfirmMealOrderOnPaymentCapturedHandler")]
public sealed class ConfirmMealOrderOnPaymentCapturedHandler
    : IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    /// <summary>La valeur que porte un paiement de repas. Voir le contrat de paiement.</summary>
    internal const string TypeDeCommande = "FOOD";

    private readonly ISender _sender;
    private readonly ILogger<ConfirmMealOrderOnPaymentCapturedHandler> _logger;

    public ConfirmMealOrderOnPaymentCapturedHandler(
        ISender sender, ILogger<ConfirmMealOrderOnPaymentCapturedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(integrationEvent.OrderType, TypeDeCommande, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var resultat = await _sender.Send(
            new ConfirmMealOrderPaymentCommand(integrationEvent.OrderId), cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "confirmer le paiement de la commande de repas — LE CLIENT A ÉTÉ DÉBITÉ",
            integrationEvent.OrderId, integrationEvent.PaymentId);
    }
}

/// <summary>
/// À l'échec du paiement, la commande est annulée.
/// </summary>
/// <remarks>
/// MOINS GRAVE QUE SON JUMEAU MARKETPLACE, ET IL FAUT LE DIRE.
///
/// Là-bas, l'échec immobilise du stock : des articles disponibles cessent d'être
/// vendables sans que rien ne l'explique. Ici il n'y a rien à libérer — un plat
/// ne se réserve pas. Ce qui reste est une commande fantôme en attente de
/// paiement, qui bloquerait le panier suivant du même client.
/// </remarks>
[NomDeConsommateur("HBA.FoodOrders.Application.Orders.EventHandlers.CancelMealOrderOnPaymentFailedHandler")]
public sealed class CancelMealOrderOnPaymentFailedHandler
    : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CancelMealOrderOnPaymentFailedHandler> _logger;

    public CancelMealOrderOnPaymentFailedHandler(
        ISender sender, ILogger<CancelMealOrderOnPaymentFailedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                integrationEvent.OrderType,
                ConfirmMealOrderOnPaymentCapturedHandler.TypeDeCommande,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // RequesterId laissé nul : c'est le système qui annule, pas le client.
        var resultat = await _sender.Send(
            new CancelMealOrderCommand(
                integrationEvent.OrderId, $"Paiement échoué : {integrationEvent.Reason}"),
            cancellationToken);

        SagaOutcome.Exiger(
            resultat, _logger,
            "annuler la commande de repas après échec du paiement",
            integrationEvent.OrderId);
    }
}
