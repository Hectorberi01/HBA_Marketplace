using Microsoft.Extensions.Logging;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Policies;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using ReturnAggregate = HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest.ReturnRequest;

namespace HBA.Marketplace.ReturnRefund.Application.Commands;

public sealed record CancelReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;
public sealed record AddEvidenceCommand(Guid ReturnId, string MediaId, string Kind, string? Caption, Guid? ActorId) : ICommand;
public sealed record ApproveReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;
public sealed record RejectReturnCommand(Guid ReturnId, string Reason, Guid? ActorId) : ICommand;
public sealed record RegisterReturnShipmentCommand(Guid ReturnId, string DeliveryId, string Mode, string? TrackingNumber, Guid? ActorId) : ICommand;
public sealed record ReceiveReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;
public sealed record InspectReturnCommand(Guid ReturnId, Domain.Enums.InspectionCondition Condition, Domain.Enums.StockDisposition Disposition, string Notes, Guid? ActorId) : ICommand;
public sealed record DecideRefundCommand(Guid ReturnId, decimal Amount, string Currency, Guid? ActorId) : ICommand;
public sealed record ExecuteRefundCommand(Guid ReturnId, Guid RefundId) : ICommand;
public sealed record CloseReturnCommand(Guid ReturnId, Guid? ActorId) : ICommand;

internal abstract class ReturnCommandHandlerBase
{
    protected ReturnCommandHandlerBase(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
    {
        Returns = returns;
        UnitOfWork = unitOfWork;
        Clock = clock;
    }

    protected IReturnRequestRepository Returns { get; }
    protected IReturnRefundUnitOfWork UnitOfWork { get; }
    protected IClock Clock { get; }
}

internal sealed class CancelReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<CancelReturnCommand>
{
    public CancelReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(CancelReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Cancel(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class AddEvidenceCommandHandler : ReturnCommandHandlerBase, ICommandHandler<AddEvidenceCommand>
{
    private readonly IMediaGrpcClient _media;

    public AddEvidenceCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock, IMediaGrpcClient media)
        : base(returns, unitOfWork, clock) => _media = media;

    public async Task<Result> Handle(AddEvidenceCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var media = await _media.ValidateMediaAsync(command.MediaId, request.CustomerId, cancellationToken);
        if (media.IsFailure) return media;
        var result = request.AddEvidence(command.MediaId, command.Kind, command.Caption, Clock.UtcNow);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ApproveReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<ApproveReturnCommand>
{
    public ApproveReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(ApproveReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Approve(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RejectReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<RejectReturnCommand>
{
    public RejectReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(RejectReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Reject(command.Reason, Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Enregistre l'expédition de retour, en créant la course d'enlèvement si
/// l'appelant n'en fournit pas.
/// </summary>
internal sealed class RegisterReturnShipmentCommandHandler : ReturnCommandHandlerBase, ICommandHandler<RegisterReturnShipmentCommand>
{
    private readonly IDeliveryGrpcClient _delivery;
    private readonly ILogger<RegisterReturnShipmentCommandHandler> _logger;

    public RegisterReturnShipmentCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock,
        IDeliveryGrpcClient delivery,
        ILogger<RegisterReturnShipmentCommandHandler> logger)
        : base(returns, unitOfWork, clock)
    {
        _delivery = delivery;
        _logger = logger;
    }

    public async Task<Result> Handle(RegisterReturnShipmentCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));

        // RÉÉCRIT : le filtrage par motif portait sur une expression dont le type
        // unifié était TOUJOURS `Result<string>`, si bien que `deliveryId is
        // Result<string>` était vrai dans les deux branches et que la branche «
        // identifiant fourni » repassait par `ok.Value`.
        string course;
        var creee = false;

        if (string.IsNullOrWhiteSpace(command.DeliveryId))
        {
            var creation = await _delivery.CreateReturnDeliveryAsync(
                request.Id, request.OrderId, request.SellerId, request.CustomerId, cancellationToken);

            if (creation.IsFailure)
            {
                return Result.Failure(creation.Error);
            }

            course = creation.Value;
            creee = true;
        }
        else
        {
            course = command.DeliveryId;
        }

        var registered = request.RegisterShipment(course, command.Mode, command.TrackingNumber, Clock.UtcNow, command.ActorId);
        if (registered.IsFailure) return registered;

        try
        {
            await UnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (creee)
        {
            _logger.LogCritical(
                exception,
                "Retour {ReturnId} : la course d'enlèvement {DeliveryId} a été CRÉÉE et l'enregistrement "
                + "a échoué. Le dossier ignore cette course : un coursier peut être dépêché sans que rien "
                + "ne le relie au retour. Annulation ou rattachement manuel requis.",
                request.Id, course);

            throw;
        }

        return Result.Success();
    }
}

internal sealed class ReceiveReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<ReceiveReturnCommand>
{
    public ReceiveReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(ReceiveReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Receive(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Inspecte la marchandise revenue et décide de son sort (remise en rayon, mise au
/// rebut…).
/// </summary>
internal sealed class InspectReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<InspectReturnCommand>
{
    private readonly IInventoryGrpcClient _inventory;
    private readonly ILogger<InspectReturnCommandHandler> _logger;

    public InspectReturnCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock,
        IInventoryGrpcClient inventory,
        ILogger<InspectReturnCommandHandler> logger)
        : base(returns, unitOfWork, clock)
    {
        _inventory = inventory;
        _logger = logger;
    }

    public async Task<Result> Handle(InspectReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));

        var result = request.Inspect(command.Condition, command.Disposition, command.Notes, Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;

        await UnitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var item in request.Items)
        {
            var stock = await _inventory.ProcessReturnedStockAsync(request.Id, item.OrderItemId, command.Disposition, cancellationToken);

            if (stock.IsFailure)
            {
                _logger.LogCritical(
                    "Retour {ReturnId} inspecté ({Disposition}), mais la ligne {OrderItemId} n'a PAS été "
                    + "traitée par l'inventaire — {Code} : {Message}. La marchandise est revenue et "
                    + "n'existe pas en stock : reprise manuelle requise.",
                    request.Id, command.Disposition, item.OrderItemId, stock.Error.Code, stock.Error.Message);
            }
        }

        return Result.Success();
    }
}

/// <summary>Fixe le montant rendu au client.</summary>
internal sealed class DecideRefundCommandHandler : ReturnCommandHandlerBase, ICommandHandler<DecideRefundCommand>
{
    private readonly IOrderGrpcClient _orders;

    public DecideRefundCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock,
        IOrderGrpcClient orders)
        : base(returns, unitOfWork, clock) => _orders = orders;

    public async Task<Result> Handle(DecideRefundCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));

        var amount = Money.Create(command.Amount, command.Currency);
        if (amount.IsFailure) return Result.Failure(amount.Error);

        var order = await _orders.GetOrderReturnContextAsync(request.OrderId, cancellationToken);
        if (order.IsFailure) return Result.Failure(order.Error);

        // Comparer des montants de devises différentes n'a aucun sens, et le faire
        // silencieusement donnerait un plafond en francs pour une saisie en euros.
        if (!string.Equals(order.Value.Currency, amount.Value.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Validation(
                "refund.currency_mismatch",
                "La devise du remboursement ne correspond pas a celle de la commande."));
        }

        // Ce que la COMMANDE peut encore rendre, tous dossiers de retour confondus.
        var plafondCommande = order.Value.CapturedAmount - order.Value.AlreadyRefundedAmount;

        // Les AUTRES dossiers ouverts sur cette commande — ceux qu'order-service ne
        // voit pas encore.
        var autresDossiers = await Returns.ListOpenQuantitiesByOrderAsync(
            request.OrderId, exceptReturnId: request.Id, cancellationToken);

        var result = request.DecideRefund(
            amount.Value,
            LignesRemboursables(request, order.Value, autresDossiers),
            plafondCommande,
            Clock.UtcNow,
            command.ActorId);

        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Croise les lignes du RETOUR avec celles de la COMMANDE pour obtenir, ligne à
    /// ligne, la quantité qu'on accepte de reprendre et son prix unitaire payé.
    /// </summary>
    private static IReadOnlyCollection<RefundableLine> LignesRemboursables(
        ReturnAggregate request,
        OrderReturnContext order,
        IReadOnlyDictionary<Guid, int> autresDossiersOuverts)
    {
        var lignes = new List<RefundableLine>();

        // La devise du dossier, déjà normalisée à l'ouverture.
        var devise = request.Currency;

        foreach (var item in request.Items)
        {
            var ligne = order.Lines.FirstOrDefault(l => l.OrderItemId == item.OrderItemId);
            if (ligne is null)
            {
                continue;
            }

            var reprise = item.ReceivedQuantity > 0 ? item.ReceivedQuantity : item.RequestedQuantity;

            // `AlreadyReturnedQuantity` compte les retours ABOUTIS ; les autres
            // dossiers encore ouverts sur la même ligne, order-service ne les voit
            // pas.
            var ouvertAilleurs = autresDossiersOuverts.TryGetValue(item.OrderItemId, out var q) ? q : 0;
            var disponible = Math.Max(0, ligne.DeliveredQuantity - ligne.AlreadyReturnedQuantity - ouvertAilleurs);
            var quantite = Math.Clamp(reprise, 0, disponible);

            if (quantite == 0)
            {
                continue;
            }

            lignes.Add(new RefundableLine(quantite, new Money(ligne.UnitPaidAmount, devise)));
        }

        return lignes;
    }
}

internal sealed class CloseReturnCommandHandler : ReturnCommandHandlerBase, ICommandHandler<CloseReturnCommand>
{
    public CloseReturnCommandHandler(IReturnRequestRepository returns, IReturnRefundUnitOfWork unitOfWork, IClock clock)
        : base(returns, unitOfWork, clock) { }

    public async Task<Result> Handle(CloseReturnCommand command, CancellationToken cancellationToken)
    {
        var request = await Returns.GetAsync(command.ReturnId, cancellationToken);
        if (request is null) return Result.Failure(Error.NotFound("return.not_found", "Retour introuvable."));
        var result = request.Close(Clock.UtcNow, command.ActorId);
        if (result.IsFailure) return result;
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
