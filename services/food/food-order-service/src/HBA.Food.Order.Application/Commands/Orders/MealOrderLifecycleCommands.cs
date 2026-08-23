using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Domain.Orders;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.FoodOrders.Application.Orders.Commands;

/// <summary>Le paiement est encaissé : la commande est confirmée.</summary>
public sealed record ConfirmMealOrderPaymentCommand(Guid OrderId) : ICommand;

/// <summary>
/// Annulation avant confirmation.
/// </summary>
/// <param name="RequesterId">
/// L'acheteur, quand c'est lui qui annule. Nul quand c'est le système — un échec
/// de paiement, par exemple. Une commande dont le demandeur n'est pas
/// propriétaire est « introuvable », comme à la lecture : distinguer révélerait
/// l'existence.
/// </param>
public sealed record CancelMealOrderCommand(
    Guid OrderId, string Reason, Guid? RequesterId = null) : ICommand;

/// <summary>La cuisine a refusé, après le paiement.</summary>
public sealed record RejectMealOrderByRestaurantCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>Le repas a été remis au client.</summary>
public sealed record MarkMealOrderDeliveredCommand(Guid OrderId) : ICommand;

/// <summary>La commande devient inexécutable : elle entre en arbitrage.</summary>
public sealed record PutMealOrderUnderReviewCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>L'arbitrage relance la commande.</summary>
public sealed record ResumeMealOrderAfterReviewCommand(Guid OrderId) : ICommand;

/// <summary>L'arbitrage retourne la vente : le client sera remboursé.</summary>
public sealed record RefundMealOrderAfterReviewCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>
/// Le socle commun des transitions : relire la commande, appliquer, persister.
///
/// ÉCRIT UNE FOIS PARCE QUE LA PARTIE INTÉRESSANTE EST AILLEURS.
///
/// Chacune de ces commandes tient en trois lignes une fois la lecture et la
/// sauvegarde factorisées. Ce qui mérite d'être lu, ce sont les gardes du
/// domaine — pourquoi une commande confirmée ne s'annule pas, pourquoi un refus
/// après paiement est une issue prévue — et elles vivent dans
/// <see cref="MealOrder"/>, pas ici.
/// </summary>
internal abstract class MealOrderTransitionHandler
{
    protected MealOrderTransitionHandler(IMealOrderRepository orders, IMealOrderUnitOfWork unitOfWork)
    {
        Orders = orders;
        UnitOfWork = unitOfWork;
    }

    protected IMealOrderRepository Orders { get; }

    protected IMealOrderUnitOfWork UnitOfWork { get; }

    protected async Task<Result> AppliquerAsync(
        Guid orderId,
        Func<MealOrder, Result> transition,
        CancellationToken cancellationToken,
        Guid? requesterId = null)
    {
        var commande = await Orders.GetByIdAsync(new MealOrderId(orderId), cancellationToken);

        if (commande is null || (requesterId is { } demandeur && commande.BuyerId != demandeur))
        {
            return Result.Failure(Error.NotFound("food_ordering.not_found", "Commande introuvable."));
        }

        var resultat = transition(commande);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ConfirmMealOrderPaymentCommandHandler
    : MealOrderTransitionHandler, ICommandHandler<ConfirmMealOrderPaymentCommand>
{
    public ConfirmMealOrderPaymentCommandHandler(
        IMealOrderRepository orders, IMealOrderUnitOfWork unitOfWork)
        : base(orders, unitOfWork)
    {
    }

    /// <summary>
    /// DEUX TRANSITIONS DANS LE MÊME ENREGISTREMENT, ET AUCUN APPEL À INVENTORY.
    ///
    /// Son équivalent marketplace intercale, entre `MarkPaid` et `Confirm`, une
    /// boucle qui solde les réservations de stock. Un repas n'en a aucune : le
    /// commentaire de `RequiresStockReservation` disait déjà que soumettre un plat
    /// à Inventory était inoffensif « par accident », au bon vouloir d'une
    /// validation vivant dans un autre module. Ici la question ne se pose plus.
    /// </summary>
    public Task<Result> Handle(ConfirmMealOrderPaymentCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(
            command.OrderId,
            commande =>
            {
                var paye = commande.MarkPaid();
                return paye.IsFailure ? paye : commande.Confirm();
            },
            cancellationToken);
}

internal sealed class CancelMealOrderCommandHandler
    : MealOrderTransitionHandler, ICommandHandler<CancelMealOrderCommand>
{
    public CancelMealOrderCommandHandler(IMealOrderRepository orders, IMealOrderUnitOfWork unitOfWork)
        : base(orders, unitOfWork)
    {
    }

    public Task<Result> Handle(CancelMealOrderCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(
            command.OrderId,
            commande => commande.Cancel(
                string.IsNullOrWhiteSpace(command.Reason) ? "Annulée par l'utilisateur." : command.Reason),
            cancellationToken,
            command.RequesterId);
}

internal sealed class RejectMealOrderByRestaurantCommandHandler
    : MealOrderTransitionHandler, ICommandHandler<RejectMealOrderByRestaurantCommand>
{
    public RejectMealOrderByRestaurantCommandHandler(
        IMealOrderRepository orders, IMealOrderUnitOfWork unitOfWork)
        : base(orders, unitOfWork)
    {
    }

    public Task<Result> Handle(
        RejectMealOrderByRestaurantCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(
            command.OrderId,
            commande => commande.RejectByRestaurant(
                string.IsNullOrWhiteSpace(command.Reason) ? "Refusée par le restaurant." : command.Reason),
            cancellationToken);
}

internal sealed class MarkMealOrderDeliveredCommandHandler
    : MealOrderTransitionHandler, ICommandHandler<MarkMealOrderDeliveredCommand>
{
    public MarkMealOrderDeliveredCommandHandler(
        IMealOrderRepository orders, IMealOrderUnitOfWork unitOfWork)
        : base(orders, unitOfWork)
    {
    }

    public Task<Result> Handle(MarkMealOrderDeliveredCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(command.OrderId, commande => commande.MarkDelivered(), cancellationToken);
}

/// <summary>
/// Les trois gestes de l'arbitrage : y entrer, en sortir par la reprise, en
/// sortir par le retour.
/// </summary>
internal sealed class MealOrderReviewCommandHandler
    : MealOrderTransitionHandler,
      ICommandHandler<PutMealOrderUnderReviewCommand>,
      ICommandHandler<ResumeMealOrderAfterReviewCommand>,
      ICommandHandler<RefundMealOrderAfterReviewCommand>
{
    public MealOrderReviewCommandHandler(IMealOrderRepository orders, IMealOrderUnitOfWork unitOfWork)
        : base(orders, unitOfWork)
    {
    }

    public Task<Result> Handle(PutMealOrderUnderReviewCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(
            command.OrderId,
            commande => commande.MarkUnderReview(
                string.IsNullOrWhiteSpace(command.Reason) ? "Commande devenue inexécutable." : command.Reason),
            cancellationToken);

    public Task<Result> Handle(ResumeMealOrderAfterReviewCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(command.OrderId, commande => commande.ResumeAfterReview(), cancellationToken);

    public Task<Result> Handle(RefundMealOrderAfterReviewCommand command, CancellationToken cancellationToken)
        => AppliquerAsync(
            command.OrderId,
            commande => commande.CancelAfterReview(
                string.IsNullOrWhiteSpace(command.Reason) ? "Retour décidé après arbitrage." : command.Reason),
            cancellationToken);
}
