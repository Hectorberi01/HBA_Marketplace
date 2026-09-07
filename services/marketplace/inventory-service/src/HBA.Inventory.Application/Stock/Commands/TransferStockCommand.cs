using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Domain.Stock;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Inventory.Application.Stock.Commands;

/// <summary>Déplace du stock d'un lieu d'expédition vers un autre.</summary>
public sealed record TransferStockCommand(
    Guid SourceItemId,
    Guid DestinationItemId,
    int Quantity,
    Guid? ActorUserId = null,
    string? Reason = null) : ICommand;

internal sealed class TransferStockCommandHandler : ICommandHandler<TransferStockCommand>
{
    private readonly IInventoryItemRepository _items;
    private readonly IStockMovementRepository _movements;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public TransferStockCommandHandler(
        IInventoryItemRepository items,
        IStockMovementRepository movements,
        IInventoryUnitOfWork unitOfWork)
    {
        _items = items;
        _movements = movements;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(TransferStockCommand command, CancellationToken cancellationToken)
    {
        var source = await _items.GetByIdAsync(
            new InventoryItemId(command.SourceItemId), cancellationToken);

        var destination = await _items.GetByIdAsync(
            new InventoryItemId(command.DestinationItemId), cancellationToken);

        // Même réponse pour les deux : dire lequel des deux manque renseignerait
        // sur l'existence d'un article qu'on n'a peut-être pas le droit de voir.
        if (source is null || destination is null)
        {
            return Result.Failure(Error.NotFound(
                "inventory.item.not_found", "Article de stock introuvable."));
        }

        var transfert = InventoryItem.Transfer(
            source, destination, command.Quantity,
            command.ActorUserId, command.Reason, DateTime.UtcNow);

        if (transfert.IsFailure)
        {
            return Result.Failure(transfert.Error);
        }

        // LES DEUX MOUVEMENTS DANS LA MÊME UNITÉ DE TRAVAIL QUE LES DEUX MUTATIONS.
        // N'en écrire qu'un — ou les écrire après coup — produirait un journal où
        // de la marchandise apparaît ou disparaît sans contrepartie, c'est-à-dire
        // un journal dont on ne peut rien conclure.
        await _movements.AddAsync(transfert.Value.Sortie, cancellationToken);
        await _movements.AddAsync(transfert.Value.Entree, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
