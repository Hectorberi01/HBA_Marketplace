using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Application.Stock.Commands;

/// <summary>Efface les réservations TERMINÉES trop anciennes pour servir encore.</summary>
/// <param name="Retention">Âge minimum d'une ligne terminée pour être effacée.</param>
/// <param name="BatchSize">Nombre maximum de lignes effacées en un tour.</param>
public sealed record PurgeStockReservationsCommand(TimeSpan Retention, int BatchSize = 500)
    : ICommand<int>;

internal sealed class PurgeStockReservationsCommandHandler
    : ICommandHandler<PurgeStockReservationsCommand, int>
{
    private readonly IInventoryItemRepository _repository;

    public PurgeStockReservationsCommandHandler(IInventoryItemRepository repository)
        => _repository = repository;

    public async Task<Result<int>> Handle(
        PurgeStockReservationsCommand command, CancellationToken cancellationToken)
    {
        if (command.Retention <= TimeSpan.Zero || command.BatchSize <= 0)
        {
            // ON REFUSE PLUTÔT QUE DE RETOMBER SUR UN DÉFAUT. Une rétention nulle
            // effacerait l'historique du jour même, y compris des lignes qu'un
            // rejeu peut encore réclamer.
            return Result.Failure<int>(Error.Validation(
                "inventory.purge.invalid_settings",
                "La rétention et la taille de lot doivent être strictement positives."));
        }

        var avant = DateTime.UtcNow - command.Retention;

        return await _repository.PurgeTerminalReservationsAsync(
            avant, command.BatchSize, cancellationToken);
    }
}
