using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Contracts;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;

// Même alias que `PlaceOrderCommandHandler` : « Order » se résout mal sous l'espace
// englobant `HBA.Orders.…`, et le compilateur ne le signale qu'à la ligne suivante,
// sur une conversion impossible.
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Application.Orders.Commands;

/// <summary>
/// Confirme le paiement d'une commande (webhook PSP / admin) : solde le stock et
/// confirme.
/// </summary>
public sealed record ConfirmOrderPaymentCommand(Guid OrderId, Guid PaymentId) : ICommand;

/// <summary>Annule une commande et libère ses réservations (compensation).</summary>
/// <param name="RequesterId">MÊME CONVENTION QUE `GetOrderQuery` : null = le système.</param>
public sealed record CancelOrderCommand(
    Guid OrderId, string Reason, Guid? RequesterId = null) : ICommand;

/// <summary>Le prestataire — un restaurant — a refusé la commande après sa confirmation.</summary>
public sealed record RejectOrderByProviderCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>Marque une commande comme livrée (déclenche escrow + payout en aval).</summary>
public sealed record MarkOrderDeliveredCommand(Guid OrderId) : ICommand;

/// <summary>LA SORTIE DE SECOURS DE LA SAGA : la commande est payée mais plus exécutable.</summary>
public sealed record PutOrderUnderReviewCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>
/// L'exploitation relance la commande : elle redevient confirmée et une nouvelle
/// course sera demandée par le composition root.
/// </summary>
public sealed record ResumeOrderAfterReviewCommand(Guid OrderId) : ICommand;

/// <summary>
/// L'exploitation retourne la vente : la commande est annulée et financial-service
/// remboursera en consommant <c> OrderCancelled</c>.
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
/// Les trois gestes de l'arbitrage : y entrer, en sortir par la reprise, en sortir
/// par le retour.
/// </summary>
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

        // Solde des réservations -> décrément du stock physique. LES LIGNES DE
        // REPAS N'ONT RIEN RÉSERVÉ : ELLES N'ONT RIEN À CONFIRMER NI À LIBÉRER.
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

    /// <summary>Crée une part par vendeur, une seule fois.</summary>
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

    // Une libération de stock qui échoue après une annulation committée ne remonte
    // nulle part : ce journal est la SEULE trace du stock resté bloqué.
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

        // La commande d'un tiers est « introuvable » — comme à la lecture, pour que
        // l'échec ne révèle pas l'existence.
        if (order is null || (command.RequesterId is { } requesterId && order.BuyerId != requesterId))
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var cancel = order.Cancel(string.IsNullOrWhiteSpace(command.Reason) ? "Annulée par l'utilisateur." : command.Reason);
        if (cancel.IsFailure)
        {
            return cancel;
        }

        // ON PERSISTE L'ANNULATION AVANT DE LIBÉRER LE STOCK (ISSUE-032).
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
                // remboursement.
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
