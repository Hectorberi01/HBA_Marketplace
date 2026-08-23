using FluentValidation;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock.Commands;

// ---- Création d'un article de stock --------------------------------------

/// <summary>Crée un article de stock pour un SKU sur une localisation.</summary>
public sealed record CreateInventoryItemCommand(string Sku, Guid LocationId, int OnHand, int ReorderThreshold) : ICommand<Guid>;

public sealed class CreateInventoryItemCommandValidator : AbstractValidator<CreateInventoryItemCommand>
{
    public CreateInventoryItemCommandValidator()
    {
        RuleFor(c => c.Sku).NotEmpty().MaximumLength(64);
        RuleFor(c => c.LocationId).NotEmpty();
        RuleFor(c => c.OnHand).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ReorderThreshold).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateInventoryItemCommandHandler : ICommandHandler<CreateInventoryItemCommand, Guid>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public CreateInventoryItemCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateInventoryItemCommand command, CancellationToken cancellationToken)
    {
        var skuResult = Sku.Create(command.Sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure<Guid>(skuResult.Error);
        }

        if (await _repository.ExistsAsync(skuResult.Value.Value, command.LocationId, cancellationToken))
        {
            return Error.Conflict("inventory.item.duplicate", "Un stock existe déjà pour ce SKU sur cette localisation.");
        }

        var result = InventoryItem.Create(skuResult.Value, command.LocationId, command.OnHand, command.ReorderThreshold);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}

// ---- Réception de stock ---------------------------------------------------

/// <summary>
/// Ajoute du stock physique (réception).
/// </summary>
/// <remarks>
/// `ActorUserId` ET `Reason` SONT NOUVEAUX (ISSUE-044) : la commande portait
/// deux champs — l'article et la quantité — et rien ne gardait trace de l'entrée.
/// L'acteur est IMPOSÉ PAR L'ENDPOINT depuis le jeton, jamais lu dans le corps.
/// </remarks>
public sealed record ReceiveStockCommand(
    Guid InventoryItemId, int Quantity, Guid? ActorUserId = null, string? Reason = null) : ICommand;

internal sealed class ReceiveStockCommandHandler : ICommandHandler<ReceiveStockCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ReceiveStockCommandHandler(
        IInventoryItemRepository repository,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _movements = movements;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReceiveStockCommand command, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(new InventoryItemId(command.InventoryItemId), cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.Receive(
            command.Quantity, command.ActorUserId, command.Reason, DateTime.UtcNow);

        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        // MÊME TRANSACTION QUE LA MUTATION. Un journal écrit après coup — ou
        // dans un gestionnaire d'événement — laisserait, au premier incident, un
        // stock modifié sans ligne qui l'explique : exactement l'état qu'on
        // referme.
        await _movements.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Ajustement de stock --------------------------------------------------

/// <summary>
/// Ajuste le stock physique (inventaire, casse, retour).
/// </summary>
/// <remarks>
/// LE MOTIF EST TOUT L'INTÉRÊT DE CE GESTE, ET IL MANQUAIT.
///
/// La commande portait `(InventoryItemId, Delta)`. Un stock passant de 400 à 12
/// ne laissait donc aucune trace de qui, quand, ni pourquoi — sur l'opération
/// précisément destinée à consigner une casse, un inventaire ou un retour abîmé.
/// </remarks>
public sealed record AdjustStockCommand(
    Guid InventoryItemId, int Delta, Guid? ActorUserId = null, string? Reason = null) : ICommand;

internal sealed class AdjustStockCommandHandler : ICommandHandler<AdjustStockCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public AdjustStockCommandHandler(
        IInventoryItemRepository repository,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _movements = movements;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AdjustStockCommand command, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(new InventoryItemId(command.InventoryItemId), cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.AdjustOnHand(
            command.Delta, command.ActorUserId, command.Reason, DateTime.UtcNow);

        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        await _movements.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Seuil de réapprovisionnement -----------------------------------------

/// <summary>Modifie le seuil d'alerte de réapprovisionnement.</summary>
public sealed record SetReorderThresholdCommand(Guid InventoryItemId, int Threshold) : ICommand;

internal sealed class SetReorderThresholdCommandHandler : ICommandHandler<SetReorderThresholdCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public SetReorderThresholdCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetReorderThresholdCommand command, CancellationToken cancellationToken)
    {
        var item = await _repository.GetByIdAsync(new InventoryItemId(command.InventoryItemId), cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.SetReorderThreshold(command.Threshold);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
