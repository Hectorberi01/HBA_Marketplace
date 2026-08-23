using FluentValidation;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock.Commands;

// ---- Réservation ----------------------------------------------------------

/// <summary>Réserve du stock d'un SKU sur une localisation pour une commande.</summary>
public sealed record ReserveStockCommand(string Sku, Guid LocationId, Guid OrderId, int Quantity, int ExpiresInMinutes = 15) : ICommand;

public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(c => c.Sku).NotEmpty();
        RuleFor(c => c.LocationId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.ExpiresInMinutes).GreaterThan(0);
    }
}

internal sealed class ReserveStockCommandHandler : ICommandHandler<ReserveStockCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ReserveStockCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReserveStockCommand command, CancellationToken cancellationToken)
    {
        var item = await ResolveItem(_repository, command.Sku, command.LocationId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.Reserve(command.OrderId, command.Quantity, DateTime.UtcNow.AddMinutes(command.ExpiresInMinutes));
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static async Task<InventoryItem?> ResolveItem(
        IInventoryItemRepository repository, string sku, Guid locationId, CancellationToken ct)
    {
        var skuResult = Sku.Create(sku);
        return skuResult.IsFailure ? null : await repository.GetBySkuAndLocationAsync(skuResult.Value.Value, locationId, ct);
    }
}

// ---- Libération -----------------------------------------------------------

/// <summary>Libère la réservation d'une commande (paiement échoué / annulation).</summary>
public sealed record ReleaseReservationCommand(string Sku, Guid LocationId, Guid OrderId) : ICommand;

internal sealed class ReleaseReservationCommandHandler : ICommandHandler<ReleaseReservationCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ReleaseReservationCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReleaseReservationCommand command, CancellationToken cancellationToken)
    {
        var item = await ReserveStockCommandHandler.ResolveItem(_repository, command.Sku, command.LocationId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        // SEULES LES RÉSERVATIONS `Active` SONT RENDUES À LA VENTE. Une
        // réservation CONFIRMÉE est du stock déjà vendu et déjà décrémenté : la
        // relâcher reviendrait à le vendre deux fois. La garde est dans l'agrégat
        // (`InventoryItem.ReleaseReservation`), pas ici — c'est elle qui protège
        // aussi les autres appelants, à commencer par `InventoryGrpcService`.
        item.ReleaseReservation(command.OrderId, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Confirmation (vente) -------------------------------------------------

/// <summary>Confirme la vente : décrémente le stock physique et solde la réservation.</summary>
public sealed record ConfirmReservationCommand(string Sku, Guid LocationId, Guid OrderId) : ICommand;

internal sealed class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ConfirmReservationCommandHandler(IInventoryItemRepository repository, IStockMovementRepository movements, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _movements = movements;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmReservationCommand command, CancellationToken cancellationToken)
    {
        var item = await ReserveStockCommandHandler.ResolveItem(_repository, command.Sku, command.LocationId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.ConfirmReservation(command.OrderId, DateTime.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        // NUL sur un rejeu : la réservation était déjà confirmée, `OnHand` n'a pas
        // bougé, et écrire une ligne de journal ferait apparaître autant de sorties
        // fantômes que de rejeux Kafka.
        if (result.Value is { } mouvement)
        {
            await _movements.AddAsync(mouvement, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
