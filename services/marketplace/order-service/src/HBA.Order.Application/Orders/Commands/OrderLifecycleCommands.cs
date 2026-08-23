using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Contracts;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;

// Même alias que `PlaceOrderCommandHandler` : « Order » se résout mal sous
// l'espace englobant `HBA.Orders.…`, et le compilateur ne le signale qu'à la
// ligne suivante, sur une conversion impossible.
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Application.Orders.Commands;

/// <summary>Confirme le paiement d'une commande (webhook PSP / admin) : solde le stock et confirme.</summary>
public sealed record ConfirmOrderPaymentCommand(Guid OrderId, Guid PaymentId) : ICommand;

/// <summary>
/// Annule une commande et libère ses réservations (compensation).
/// </summary>
/// <param name="RequesterId">
/// MÊME CONVENTION QUE `GetOrderQuery` : null = le système.
///
/// La compensation d'un paiement échoué passe par ici sans utilisateur ; la
/// route HTTP, elle, doit prouver que l'appelant est bien l'acheteur. Sans quoi
/// n'importe quel inscrit annulait la commande d'un tiers — et déclenchait au
/// passage son remboursement, `RefundPaymentOnOrderCancelledHandler` étant
/// branché sur l'annulation.
/// </param>
public sealed record CancelOrderCommand(
    Guid OrderId, string Reason, Guid? RequesterId = null) : ICommand;

/// <summary>
/// Le prestataire — un restaurant — a refusé la commande après sa confirmation.
///
/// DISTINCTE DE `CancelOrderCommand`, ET ELLE DOIT LE RESTER.
///
/// L'annulation ordinaire refuse une commande confirmée : c'est une vente
/// conclue, et revenir dessus est un retour. La restauration inverse la
/// chronologie — le client paie, PUIS la cuisine décide — et un refus y est une
/// issue prévue, pas un incident. Fondre les deux rouvrirait un chemin
/// d'annulation sur les ventes de marchandise déjà conclues.
///
/// CETTE COMMANDE NE REMBOURSE PAS. Elle libère le droit ; le remboursement
/// appartient à Payments, et c'est l'adaptateur du composition root qui enchaîne
/// les deux.
/// </summary>
public sealed record RejectOrderByProviderCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>Marque une commande comme livrée (déclenche escrow + payout en aval).</summary>
public sealed record MarkOrderDeliveredCommand(Guid OrderId) : ICommand;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA SORTIE DE SECOURS DE LA SAGA : la commande est payée mais plus exécutable.
///
/// ELLE N'ANNULE PAS ET NE REMBOURSE PAS.
///
/// Elle fait sortir la commande du « en cours » et la pose dans une file où un
/// humain tranchera. Sans elle, une course annulée ou une expédition multi-lieux
/// laissait la commande `Confirmed` pour toujours : ni livraison, ni annulation,
/// ni remboursement, escrow gelé, stock déjà décrémenté.
///
/// POURQUOI PAS UN REMBOURSEMENT AUTOMATIQUE : une course annulée est le plus
/// souvent RÉATTRIBUABLE. Rembourser d'office détruirait des ventes récupérables,
/// et l'argent rendu ne se reprend pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record PutOrderUnderReviewCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>
/// L'exploitation relance la commande : elle redevient confirmée et une nouvelle
/// course sera demandée par le composition root.
/// </summary>
public sealed record ResumeOrderAfterReviewCommand(Guid OrderId) : ICommand;

/// <summary>
/// L'exploitation retourne la vente : la commande est annulée et
/// financial-service remboursera en consommant <c>OrderCancelled</c>.
///
/// DISTINCTE DE `CancelOrderCommand`, ET POUR LA MÊME RAISON QUE
/// `RejectOrderByProviderCommand` : l'annulation ordinaire libère des
/// réservations de stock qui, ici, ont DÉJÀ été soldées à la confirmation.
/// </summary>
public sealed record RefundOrderAfterReviewCommand(Guid OrderId, string Reason) : ICommand;

internal sealed class MarkOrderDeliveredCommandHandler : ICommandHandler<MarkOrderDeliveredCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public MarkOrderDeliveredCommandHandler(IOrderRepository orderRepository, IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkOrderDeliveredCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var delivered = order.MarkDelivered();
        if (delivered.IsFailure)
        {
            return delivered;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Les trois gestes de l'arbitrage : y entrer, en sortir par la reprise, en
/// sortir par le retour.
/// </summary>
/// <remarks>
/// AUCUN APPEL À INVENTORY DANS CE FICHIER, ET C'EST UN CHOIX.
///
/// `CancelOrderCommandHandler` libère les réservations en compensation. Ici, le
/// stock a été SOLDÉ à la confirmation (`ConfirmReservationAsync`) : il n'y a
/// plus de réservation à rendre. Ce qu'il y a, c'est de la marchandise à remettre
/// en rayon — un geste d'exploitation dans Inventory, sur un colis qu'on récupère
/// physiquement, et non une compensation de saga. Appeler `ReleaseReservation`
/// par symétrie ferait croire à un travail qui n'existe pas et gonflerait le
/// disponible d'articles restés chez le vendeur.
/// </remarks>
internal sealed class OrderReviewCommandHandler
    : ICommandHandler<PutOrderUnderReviewCommand>,
      ICommandHandler<ResumeOrderAfterReviewCommand>,
      ICommandHandler<RefundOrderAfterReviewCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public OrderReviewCommandHandler(IOrderRepository orderRepository, IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(PutOrderUnderReviewCommand command, CancellationToken cancellationToken)
        => MutateAsync(
            command.OrderId,
            order => order.MarkUnderReview(
                string.IsNullOrWhiteSpace(command.Reason)
                    ? "Commande devenue inexécutable."
                    : command.Reason),
            cancellationToken);

    public Task<Result> Handle(ResumeOrderAfterReviewCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.OrderId, order => order.ResumeAfterReview(), cancellationToken);

    public Task<Result> Handle(RefundOrderAfterReviewCommand command, CancellationToken cancellationToken)
        => MutateAsync(
            command.OrderId,
            order => order.CancelAfterReview(
                string.IsNullOrWhiteSpace(command.Reason)
                    ? "Arbitrage : commande non livrable, remboursement décidé."
                    : command.Reason),
            cancellationToken);

    private async Task<Result> MutateAsync(
        Guid orderId, Func<OrderAggregate, Result> transition, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(orderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var result = transition(order);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Confirme le paiement, solde le stock, confirme la commande — et DÉCOUPE la
/// commande en une part par vendeur.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DÉCOUPAGE SE FAIT ICI, ET NULLE PART AILLEURS (ISSUE-027).
///
/// C'est le SEUL appelant d'`Order.Confirm()` du dépôt — vérifié avant d'écrire.
/// Le poser ici plutôt qu'au passage de commande n'est pas un détail
/// d'implémentation : avant le paiement, il n'y a rien qu'un vendeur puisse
/// faire, et lui montrer une commande non payée l'inviterait à préparer un colis
/// pour un paiement qui échouera. C'est le même raisonnement qui fait que
/// `OrderConfirmed` — et non `OrderPlaced` — est l'événement qui prévient les
/// vendeurs.
///
/// APRÈS `Confirm()`, ET DANS LE MÊME `SaveChanges`.
///
/// Après, parce que `SellerOrder.SplitFrom` exige une commande déjà confirmée :
/// une part née sur une commande `Paid` apparaîtrait dans un carnet avant que
/// l'encaissement soit acquis. Dans la même transaction, parce qu'une commande
/// confirmée sans ses parts est exactement l'état d'avant ce lot — le vendeur
/// voit la commande arriver et n'a aucun geste à poser.
///
/// UNE COMMANDE DE REPAS N'EN PRODUIT AUCUNE, ET CE N'EST PAS UNE ERREUR.
///
/// `SplitFrom` réutilise le filtre de `BuildSellerShares`, qui écarte les lignes
/// de repas. Le restaurant travaille sur un ticket de cuisine, dans
/// food-service, avec son propre cycle d'acceptation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class ConfirmOrderPaymentCommandHandler : ICommandHandler<ConfirmOrderPaymentCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ISellerOrderRepository _sellerOrderRepository;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly IOrderingUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmOrderPaymentCommandHandler> _logger;

    public ConfirmOrderPaymentCommandHandler(
        IOrderRepository orderRepository,
        ISellerOrderRepository sellerOrderRepository,
        IInventoryModuleApi inventoryModuleApi,
        IOrderingUnitOfWork unitOfWork,
        ILogger<ConfirmOrderPaymentCommandHandler> logger)
    {
        _orderRepository = orderRepository;
        _sellerOrderRepository = sellerOrderRepository;
        _inventoryModuleApi = inventoryModuleApi;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmOrderPaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var paid = order.MarkPaid(command.PaymentId);
        if (paid.IsFailure)
        {
            return paid;
        }

        // Solde des réservations -> décrément du stock physique.
        // LES LIGNES DE REPAS N'ONT RIEN RÉSERVÉ : ELLES N'ONT RIEN À CONFIRMER
        // NI À LIBÉRER.
        //
        // Leur SKU est vide. Aujourd'hui c'est inoffensif — `Sku.Create("")`
        // échoue chez Inventory, qui répond « non suivi » sans requête. Mais
        // c'est inoffensif PAR ACCIDENT, au bon vouloir d'une validation qui vit
        // dans un autre module. Le commentaire de `RequiresStockReservation`
        // annonçait exactement cet oubli ; le voici comblé.
        foreach (var line in order.Lines.Where(l => l.RequiresStockReservation))
        {
            await _inventoryModuleApi.ConfirmReservationAsync(line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);
        }

        var confirmed = order.Confirm();
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        var decoupage = await DecouperParVendeurAsync(order, cancellationToken);
        if (decoupage.IsFailure)
        {
            return decoupage;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Crée une part par vendeur, une seule fois.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// IDEMPOTENCE : LA CONFIRMATION PEUT ÊTRE REJOUÉE.
    ///
    /// `PaymentCaptured` arrive par Kafka, qui livre AU MOINS une fois. Deux
    /// parts pour le même (commande, vendeur) doubleraient la vue du vendeur ET
    /// le montant de sa part — c'est-à-dire ce qu'il croit avoir vendu.
    ///
    /// Trois gardes se superposent, et aucune ne rend les deux autres inutiles :
    ///
    ///   1. `order.MarkPaid` refuse une commande qui n'est plus
    ///      `AwaitingPayment`. C'est la garde qui ferme le rejeu ordinaire, et
    ///      elle suffit tant que la saga passe par ce chemin-ci ;
    ///   2. cette relecture, qui tient si un autre chemin amène un jour une
    ///      commande déjà confirmée jusqu'ici ;
    ///   3. l'index unique `(OrderId, SellerId)` posé par la migration
    ///      `CommandeParVendeur` — le SEUL qui ferme la course entre deux
    ///      messages traités EN PARALLÈLE, qui lisent tous deux « rien » avant
    ///      que l'un ait écrit. La seconde insertion échoue, le message est
    ///      rejoué, et le second passage trouve les parts. C'est la même
    ///      construction que `order_return_settlements` et
    ///      `UnicitePanierParCommande`.
    ///
    /// UN REJEU N'EST PAS UNE ERREUR : ON JOURNALISE ET ON POURSUIT.
    ///
    /// Rendre un échec ferait remonter `SagaOutcome.Exiger` sur un message
    /// parfaitement normal, avec un journal qui dit « L'ACHETEUR A ÉTÉ DÉBITÉ » —
    /// une alerte pour un cas correctement traité, ce qui est la façon la plus
    /// sûre de faire ignorer les vraies.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task<Result> DecouperParVendeurAsync(OrderAggregate order, CancellationToken cancellationToken)
    {
        if (await _sellerOrderRepository.ExistsForOrderAsync(order.Id.Value, cancellationToken))
        {
            _logger.LogInformation(
                "Commande {OrderId} déjà découpée par vendeur : confirmation rejouée, "
                + "aucune part supplémentaire créée.",
                order.Id.Value);

            return Result.Success();
        }

        var parts = SellerOrder.SplitFrom(order, DateTime.UtcNow);
        if (parts.IsFailure)
        {
            return Result.Failure(parts.Error);
        }

        // Vide pour un repas : le restaurant travaille sur un ticket de cuisine,
        // pas sur une commande vendeur.
        if (parts.Value.Count == 0)
        {
            return Result.Success();
        }

        await _sellerOrderRepository.AddRangeAsync(parts.Value, cancellationToken);
        return Result.Success();
    }
}

internal sealed class RejectOrderByProviderCommandHandler : ICommandHandler<RejectOrderByProviderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public RejectOrderByProviderCommandHandler(
        IOrderRepository orderRepository, IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectOrderByProviderCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        // AUCUNE LIBÉRATION DE STOCK ICI, ET IL N'Y EN A RIEN À FAIRE.
        //
        // Une commande de repas n'a jamais réservé quoi que ce soit dans
        // Inventory — c'est tout le sens de `RequiresStockReservation`. Appeler
        // la compensation par symétrie avec `CancelOrderCommandHandler` ferait
        // croire à un travail qui n'existe pas.
        var rejet = order.RejectByProvider(
            string.IsNullOrWhiteSpace(command.Reason) ? "Refusée par le restaurant." : command.Reason);

        if (rejet.IsFailure)
        {
            return rejet;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly IOrderingUnitOfWork _unitOfWork;

    // Une libération de stock qui échoue après une annulation committée ne
    // remonte nulle part : ce journal est la SEULE trace du stock resté bloqué.
    private readonly ILogger<CancelOrderCommandHandler> _logger;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IInventoryModuleApi inventoryModuleApi,
        IOrderingUnitOfWork unitOfWork,
        ILogger<CancelOrderCommandHandler> logger)
    {
        _orderRepository = orderRepository;
        _inventoryModuleApi = inventoryModuleApi;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);

        // La commande d'un tiers est « introuvable » — comme à la lecture, pour
        // que l'échec ne révèle pas l'existence.
        if (order is null || (command.RequesterId is { } requesterId && order.BuyerId != requesterId))
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var cancel = order.Cancel(string.IsNullOrWhiteSpace(command.Reason) ? "Annulée par l'utilisateur." : command.Reason);
        if (cancel.IsFailure)
        {
            return cancel;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ON PERSISTE L'ANNULATION AVANT DE LIBÉRER LE STOCK (ISSUE-032).
        //
        // L'ordre était l'inverse : on libérait chez Inventory, PUIS on écrivait.
        // Un `SaveChangesAsync` qui lève laissait alors le stock rendu au rayon et
        // la commande TOUJOURS VIVANTE — payée, en cours d'expédition, et sans
        // réservation en face. C'est-à-dire la survente : deux acheteurs pour un
        // exemplaire, découverte à la préparation du colis.
        //
        // L'ORDRE INVERSE A UN COÛT, ET IL EST MOINDRE.
        //
        // Si la libération échoue APRÈS l'écriture, le stock reste réservé pour une
        // commande annulée : de la marchandise immobilisée à tort. C'est un manque
        // à gagner, réparable — par une reprise manuelle, et un jour par le
        // balayeur d'expiration qui manque encore (ISSUE-031). La survente, elle,
        // se paie en commande honorée qu'on ne peut pas livrer.
        //
        // Entre « du stock bloqué qu'on peut rendre » et « du stock vendu deux fois
        // qu'on ne peut pas fabriquer », le choix n'est pas serré.
        //
        // LES LIGNES DE REPAS N'ONT RIEN RÉSERVÉ : ELLES N'ONT RIEN À LIBÉRER.
        //
        // Leur SKU est vide. Aujourd'hui c'est inoffensif — `Sku.Create("")`
        // échoue chez Inventory, qui répond « non suivi » sans requête. Mais
        // c'est inoffensif PAR ACCIDENT, au bon vouloir d'une validation qui vit
        // dans un autre module. Le commentaire de `RequiresStockReservation`
        // annonçait exactement cet oubli ; le voici comblé.
        // ═════════════════════════════════════════════════════════════════════
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var line in order.Lines.Where(l => l.RequiresStockReservation))
        {
            try
            {
                await _inventoryModuleApi.ReleaseReservationAsync(line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);
            }
            catch (Exception echecLiberation)
            {
                // ON N'ANNULE PAS L'ANNULATION. Elle est committée, elle a déjà
                // publié `OrderCancelled`, et financial-service en tire un
                // remboursement. Lever ici rendrait une erreur à l'acheteur pour
                // une commande RÉELLEMENT annulée, et l'inviterait à recommencer.
                //
                // Le SKU et l'emplacement sont dans le message : c'est la seule
                // prise pour rendre ce stock à la main.
                _logger.LogCritical(
                    echecLiberation,
                    "Commande {OrderId} annulée, mais la libération du SKU {Sku} sur l'emplacement "
                    + "{LocationId} a ÉCHOUÉ. Ce stock reste réservé pour une commande annulée — "
                    + "libération manuelle requise.",
                    order.Id.Value, line.Sku, line.ShipFromLocationId);
            }
        }

        return Result.Success();
    }
}
