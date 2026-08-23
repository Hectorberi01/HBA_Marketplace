using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Orders.Domain.Orders.Events;
using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Orders.Domain.Orders.SellerOrders.Events;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>
/// Publie « la part d'un vendeur ne sera pas honorée ».
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CE PUBLICATEUR DÉCLENCHE AUJOURD'HUI : RIEN. ET IL FAUT LE LIRE ICI.
///
/// `SellerOrderRefusedIntegrationEvent` n'a AUCUN consommateur dans le dépôt —
/// vérifié sur l'ensemble des services avant d'écrire ce fichier. Un vendeur qui
/// refuse sa part d'une commande payée ne fait donc, à cette heure, ni libérer le
/// stock, ni rembourser le client, ni le prévenir. Le message part, il est
/// acquitté, il n'arrive nulle part.
///
/// Ce n'est pas une négligence, c'est le périmètre : les trois gestes manquants
/// vivent dans inventory-service, financial-service et communication-service, et
/// le plus lourd des trois — rembourser une FRACTION de commande — n'existe pas
/// encore comme capacité. Les inventer ici en appelant trois modules depuis un
/// gestionnaire d'événement de domaine serait pire : on aurait un remboursement
/// partiel écrit par le service qui ne possède ni le paiement ni le stock.
///
/// ALORS POURQUOI LE PUBLIER MAINTENANT.
///
/// Parce que le FAIT doit exister. Le jour où le premier des trois consommateurs
/// est branché, il n'y a rien à changer ici — et entre-temps, le message est dans
/// l'outbox, horodaté, corrélé, relisible. L'alternative était de ne rien lever
/// du tout : le refus n'aurait laissé qu'une ligne changée dans une table, et
/// personne n'aurait su qu'il fallait rembourser.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SellerOrderRefusedDomainEventHandler
    : IDomainEventHandler<SellerOrderRefusedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<SellerOrderRefusedDomainEventHandler> _logger;

    public SellerOrderRefusedDomainEventHandler(
        IIntegrationEventPublisher publisher, ILogger<SellerOrderRefusedDomainEventHandler> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerOrderRefusedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _publisher.PublishAsync(
            new SellerOrderRefusedIntegrationEvent
            {
                SellerOrderId = domainEvent.SellerOrderId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                SellerId = domainEvent.SellerId,
                Currency = domainEvent.Currency,
                Outcome = domainEvent.Outcome,
                Reason = domainEvent.Reason,
                Amount = domainEvent.Amount,

                // Deux records portent la même idée — l'un dans le domaine, l'autre
                // dans les Contracts — et c'est délibéré, comme pour
                // `OrderSellerShare` : le contrat public ne doit pas dépendre du
                // modèle interne d'Ordering.
                Lines = domainEvent.Lines
                    .Select(l => new HBA.Orders.Contracts.IntegrationEvents.SellerOrderRefusedLine(
                        l.OrderLineId, l.ProductId, l.Sku, l.ShipFromLocationId, l.Quantity, l.LineTotal))
                    .ToList()
            },
            cancellationToken);

        // JOURNAL D'AVERTISSEMENT, PAS D'INFORMATION, ET IL RESTE JUSQU'À CE
        // QUE LE PREMIER CONSOMMATEUR EXISTE.
        //
        // C'est la seule chose qui, aujourd'hui, met un humain au courant qu'un
        // client a payé pour quelque chose qui ne viendra pas. Le jour où le
        // remboursement de part sera câblé, cette ligne devra tomber — la laisser
        // ferait crier le journal sur un cas correctement traité, et un contrôle
        // qui crie pour rien finit ignoré.
        _logger.LogWarning(
            "Commande vendeur {SellerOrderId} ({Outcome}) : le vendeur {SellerId} n'honore pas sa part "
            + "de la commande {OrderId} — {Amount} {Currency} déjà encaissés. Motif : {Reason}. "
            + "AUCUN CONSOMMATEUR n'écoute encore cet événement : ni stock rendu, ni remboursement, "
            + "ni notification. Reprise MANUELLE requise.",
            domainEvent.SellerOrderId, domainEvent.Outcome, domainEvent.SellerId, domainEvent.OrderId,
            domainEvent.Amount, domainEvent.Currency, domainEvent.Reason);
    }
}

/// <summary>
/// La commande entière tombe : ses parts vendeur tombent avec elle.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE GESTIONNAIRE, UN VENDEUR PRÉPARE UN COLIS DÉJÀ REMBOURSÉ.
///
/// Un seul chemin annule une commande APRÈS sa confirmation, donc après que ses
/// parts existent : `CancelAfterReview`, quand l'exploitation tranche en faveur
/// du retour. Sans cascade, les parts resteraient `Confirmed` ou `Preparing` dans
/// le carnet des vendeurs, sans un mot, pour une vente dont l'argent est déjà
/// reparti. Le vendeur emballerait, la course serait demandée, et le client
/// recevrait un colis qu'il a cessé de payer.
///
/// Les autres chemins d'annulation — `Cancel` avant confirmation, un refus de
/// restaurant — ne trouvent aucune part, et ce gestionnaire ne fait rien. C'est
/// le cas courant, et il doit rester silencieux.
///
/// IL NE PUBLIE RIEN, ET C'EST LE POINT DÉLICAT.
///
/// `CancelWithOrder` ne lève AUCUN événement de refus vendeur. `OrderCancelled`
/// est déjà parti et c'est lui que financial-service consomme pour rembourser —
/// la totalité, puisque la commande entière tombe. Lever en plus un refus par
/// vendeur ferait, le jour où ce refus aura un consommateur, rembourser une
/// SECONDE fois chaque part d'une commande déjà intégralement remboursée.
///
/// ET IL NE POURRAIT PAS PUBLIER MÊME S'IL LE VOULAIT.
///
/// `ModuleDbContext` dispatche les événements de domaine AVANT
/// `base.SaveChangesAsync` : un événement levé PENDANT ce dispatch ne serait
/// jamais collecté. La contrainte technique va donc dans le même sens que la
/// règle métier — mais c'est la règle métier qui décide, et il faut le savoir
/// avant d'ajouter ici une transition qui lève quelque chose.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CancelSellerOrdersOnOrderCancelledHandler
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly ISellerOrderRepository _sellerOrders;
    private readonly ILogger<CancelSellerOrdersOnOrderCancelledHandler> _logger;

    public CancelSellerOrdersOnOrderCancelledHandler(
        ISellerOrderRepository sellerOrders, ILogger<CancelSellerOrdersOnOrderCancelledHandler> logger)
    {
        _sellerOrders = sellerOrders;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var parts = await _sellerOrders.ListByOrderAsync(domainEvent.OrderId, cancellationToken);

        foreach (var part in parts.Where(p => p.IsOpen))
        {
            // Le motif de la commande est recopié tel quel : c'est la CAUSE, et
            // c'est ce que le vendeur doit lire dans son carnet. Un motif générique
            // (« annulée ») l'obligerait à demander pourquoi.
            var resultat = part.CancelWithOrder(domainEvent.Reason, DateTime.UtcNow);

            if (resultat.IsFailure)
            {
                // ON NE LÈVE PAS : L'ANNULATION DE LA COMMANDE EST ACQUISE.
                //
                // Échouer ici ferait échouer le `SaveChanges` de l'annulation
                // elle-même, donc laisserait la commande VIVANTE — pour une part
                // vendeur qu'on n'a pas su fermer. Le déséquilibre est
                // exactement dans le mauvais sens.
                _logger.LogError(
                    "Commande {OrderId} annulée, mais la part du vendeur {SellerId} n'a pas pu être "
                    + "fermée ({Code}). Elle reste ouverte dans son carnet : fermeture manuelle requise.",
                    domainEvent.OrderId, part.SellerId, resultat.Error.Code);
            }
        }
    }
}
